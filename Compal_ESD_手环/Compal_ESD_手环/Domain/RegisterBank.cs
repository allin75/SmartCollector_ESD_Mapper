namespace Compal_ESD_手环;

internal sealed class RegisterBank
{
    private readonly object _sync = new();
    private readonly ushort[] _registers = new ushort[RegisterLayout.TotalRegisters];

    public void ApplyLine(LineMappingResult mapping)
    {
        var baseOffset = RegisterLayout.GetBaseOffset(mapping.LineName);

        lock (_sync)
        {
            Array.Clear(_registers, baseOffset, RegisterLayout.RegistersPerLine);
            Array.Copy(mapping.LineRegisters, 0, _registers, baseOffset, mapping.LineRegisters.Length);
        }
    }

    public void ClearLine(char lineName)
    {
        var baseOffset = RegisterLayout.GetBaseOffset(lineName);

        lock (_sync)
        {
            Array.Clear(_registers, baseOffset, RegisterLayout.RegistersPerLine);
        }
    }

    public ushort[] ReadRange(int startAddress, int quantity)
    {
        if (startAddress < 0 || quantity < 0 || startAddress + quantity > RegisterLayout.TotalRegisters)
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
