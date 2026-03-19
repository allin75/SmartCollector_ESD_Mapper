using System.Buffers.Binary;
using System.Globalization;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using Compal_ESD_区域静电.Core.Models;
using NModbus;
using NModbus.Serial;
using NLog;

namespace Compal_ESD_区域静电.Core.Services;

public sealed class DataCollectWorker : IDisposable
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private const int MinimumRegisterCount = 100;

    private readonly CollectorRuntimeOptions _options;
    private readonly IReadOnlyList<Mapper> _mapper;
    private readonly RegisterBank _registerBank;
    private ModbusTcpServer? _modbusTcpServer;
    private bool _serverStarted;

    public DataCollectWorker(CollectorRuntimeOptions? options = null)
    {
        _options = options ?? CollectorRuntimeOptions.Load();
        _mapper = _options.Mappings;
        _registerBank = new RegisterBank(ResolveRegisterCount(_mapper));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        EnsureServerStarted();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ReadDataFromDevicesAsync(cancellationToken);
                await Task.Delay(_options.PollingIntervalMs, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Collect loop failed");
            }
        }
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        EnsureServerStarted();
        await ReadDataFromDevicesAsync(cancellationToken);
    }

    private async Task ReadDataFromDevicesAsync(CancellationToken cancellationToken)
    {
        var availablePorts = SerialPort.GetPortNames();

        foreach (var item in _mapper)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!availablePorts.Any(p => string.Equals(p, item.PortName, StringComparison.OrdinalIgnoreCase)))
                {
                    Logger.Warn("Skip collect because port does not exist. port={0}, slaveId={1}, sourceRegisterAddress={2}", item.PortName, item.SlaveId, item.SourceRegisterAddress);
                    continue;
                }

                using var serialPort = CreateSerialPort(item);
                serialPort.Open();

                var factory = new ModbusFactory();
                using var master = factory.CreateRtuMaster(new SerialPortAdapter(serialPort));
                master.Transport.Retries = 1;
                master.Transport.ReadTimeout = 1000;
                master.Transport.WriteTimeout = 1000;

                var registers = await master.ReadHoldingRegistersAsync(item.SlaveId, item.SourceRegisterAddress, 1);
                if (registers.Length == 0)
                {
                    Logger.Warn("Read returned empty. port={0}, slaveId={1}, sourceRegisterAddress={2}", item.PortName, item.SlaveId, item.SourceRegisterAddress);
                    continue;
                }

                var value = unchecked((short)registers[0]);
                TryWriteToServer(item.TargetRegisterOffset, value);
                Logger.Info("Write value to ModbusTcpServer. targetRegisterOffset={0}, value={1}", item.TargetRegisterOffset, value);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Device collect failed. port={0}, slaveId={1}, sourceRegisterAddress={2}", item.PortName, item.SlaveId, item.SourceRegisterAddress);
            }
        }
    }

    private static SerialPort CreateSerialPort(Mapper item)
    {
        return new SerialPort(item.PortName, item.BaudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 1000,
            WriteTimeout = 1000
        };
    }

    private void TryWriteToServer(int targetRegisterOffset, object? value)
    {
        if (targetRegisterOffset < 0 || value is null)
        {
            return;
        }

        if (!short.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var shortValue))
        {
            Logger.Warn("Value cannot convert to Int16. targetRegisterOffset={0}, value={1}", targetRegisterOffset, value);
            return;
        }

        try
        {
            _registerBank.Write(targetRegisterOffset, unchecked((ushort)shortValue));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Logger.Warn(ex, "Write to register bank failed. targetRegisterOffset={0}", targetRegisterOffset);
        }
    }

    private static int ResolveRegisterCount(IEnumerable<Mapper> mapper)
    {
        var maxOffset = -1;

        foreach (var item in mapper)
        {
            if (item.TargetRegisterOffset >= 0)
            {
                maxOffset = Math.Max(maxOffset, item.TargetRegisterOffset);
            }
        }

        return Math.Max(MinimumRegisterCount, maxOffset + 1);
    }

    private void EnsureServerStarted()
    {
        if (_serverStarted)
        {
            return;
        }

        _modbusTcpServer = new ModbusTcpServer(_options.TcpPort, _registerBank);
        _modbusTcpServer.Start();
        _serverStarted = true;

        Logger.Info("ModbusTcpServer started on port {0}", _options.TcpPort);
        Logger.Info("Register count: {0}", _registerBank.Count);
        Logger.Info("Loaded mapper count: {0}", _mapper.Count);
    }

    public void Dispose()
    {
        try
        {
            _modbusTcpServer?.Dispose();
        }
        catch
        {
            // ignore dispose errors
        }
    }
}

internal sealed class RegisterBank
{
    private readonly object _sync = new();
    private readonly ushort[] _registers;

    public RegisterBank(int registerCount)
    {
        if (registerCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(registerCount), "寄存器数量必须大于 0。");
        }

        _registers = new ushort[registerCount];
    }

    public int Count => _registers.Length;

    public void Write(int address, ushort value)
    {
        if (address < 0 || address >= _registers.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(address), "写入范围超出寄存器区间。");
        }

        lock (_sync)
        {
            _registers[address] = value;
        }
    }

    public ushort[] ReadRange(int startAddress, int quantity)
    {
        if (startAddress < 0 || quantity < 0 || startAddress + quantity > _registers.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(startAddress), "读取范围超出寄存器区间。");
        }

        var values = new ushort[quantity];

        lock (_sync)
        {
            Array.Copy(_registers, startAddress, values, 0, quantity);
        }

        return values;
    }
}

internal sealed class ModbusTcpServer : IDisposable
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly TcpListener _listener;
    private readonly RegisterBank _registerBank;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoopTask;
    private bool _started;
    private bool _disposed;

    public ModbusTcpServer(int port, RegisterBank registerBank)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _registerBank = registerBank;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_started)
        {
            return;
        }

        _listener.Start();
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        _started = true;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            }
        }
        catch (Exception ex) when (!_cts.IsCancellationRequested)
        {
            Logger.Error(ex, "ModbusTcpServer accept loop failed");
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            var headerBuffer = new byte[7];

            while (!cancellationToken.IsCancellationRequested && client.Connected)
            {
                if (!await ReadExactAsync(stream, headerBuffer, cancellationToken))
                {
                    break;
                }

                var transactionId = BinaryPrimitives.ReadUInt16BigEndian(headerBuffer.AsSpan(0, 2));
                var protocolId = BinaryPrimitives.ReadUInt16BigEndian(headerBuffer.AsSpan(2, 2));
                var length = BinaryPrimitives.ReadUInt16BigEndian(headerBuffer.AsSpan(4, 2));
                var unitId = headerBuffer[6];

                if (protocolId != 0 || length < 2)
                {
                    break;
                }

                var pduLength = length - 1;
                var pduBuffer = new byte[pduLength];
                if (!await ReadExactAsync(stream, pduBuffer, cancellationToken))
                {
                    break;
                }

                var response = HandleRequest(unitId, pduBuffer);
                WriteMbapHeader(response, transactionId, unitId);
                await stream.WriteAsync(response, cancellationToken);
            }
        }
    }

    private byte[] HandleRequest(byte unitId, byte[] pdu)
    {
        if (pdu.Length == 0)
        {
            return BuildExceptionResponse(unitId, 0, 0x03);
        }

        var functionCode = pdu[0];

        return functionCode switch
        {
            0x03 or 0x04 => HandleReadRegisters(unitId, functionCode, pdu),
            _ => BuildExceptionResponse(unitId, functionCode, 0x01)
        };
    }

    private byte[] HandleReadRegisters(byte unitId, byte functionCode, byte[] pdu)
    {
        if (pdu.Length < 5)
        {
            return BuildExceptionResponse(unitId, functionCode, 0x03);
        }

        var startAddress = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(1, 2));
        var quantity = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(3, 2));

        if (quantity is 0 or > 125)
        {
            return BuildExceptionResponse(unitId, functionCode, 0x03);
        }

        if (startAddress + quantity > _registerBank.Count)
        {
            return BuildExceptionResponse(unitId, functionCode, 0x02);
        }

        var values = _registerBank.ReadRange(startAddress, quantity);
        var byteCount = quantity * 2;
        var response = new byte[7 + 2 + byteCount];
        response[6] = unitId;
        response[7] = functionCode;
        response[8] = (byte)byteCount;

        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(9 + (i * 2), 2), values[i]);
        }

        return response;
    }

    private static byte[] BuildExceptionResponse(byte unitId, byte functionCode, byte exceptionCode)
    {
        var response = new byte[9];
        response[6] = unitId;
        response[7] = (byte)(functionCode | 0x80);
        response[8] = exceptionCode;
        return response;
    }

    private static void WriteMbapHeader(byte[] response, ushort transactionId, byte unitId)
    {
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(0, 2), transactionId);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4, 2), (ushort)(response.Length - 6));
        response[6] = unitId;
    }

    private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var totalRead = 0;

        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead), cancellationToken);
            if (read == 0)
            {
                return false;
            }

            totalRead += read;
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        _listener.Stop();

        try
        {
            _acceptLoopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignore shutdown errors
        }

        _cts.Dispose();
    }
}
