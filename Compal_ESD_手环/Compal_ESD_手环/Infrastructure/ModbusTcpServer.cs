using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Compal_ESD_手环;

internal sealed class ModbusTcpServer : IDisposable
{
    private readonly int _port;
    private readonly RegisterBank _registerBank;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();

    public ModbusTcpServer(int port, RegisterBank registerBank)
    {
        _port = port;
        _registerBank = registerBank;
        _listener = new TcpListener(IPAddress.Any, _port);
    }

    public async Task StartAsync()
    {
        Console.CancelKeyPress += OnCancelKeyPress;
        _listener.Start();

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = Task.Run(() => HandleClientAsync(client, _cts.Token), _cts.Token);
            }
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
            _listener.Stop();
        }
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        _cts.Cancel();
        _listener.Stop();
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

        if (startAddress + quantity > RegisterLayout.TotalRegisters)
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
        _cts.Cancel();
        _listener.Stop();
        _cts.Dispose();
    }
}
