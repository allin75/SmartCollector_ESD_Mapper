namespace Compal_ESD_手环;

internal static class RegisterLayout
{
    public const int RegistersPerLine = 100;
    public const int LineCount = 9;
    public const int TotalRegisters = RegistersPerLine * LineCount;

    public static int GetBaseOffset(char lineName)
    {
        var normalized = char.ToUpperInvariant(lineName);
        if (normalized is < 'A' or > 'I')
        {
            throw new ArgumentOutOfRangeException(nameof(lineName), "线别必须在 A 到 I 之间。");
        }

        return (normalized - 'A') * RegistersPerLine;
    }
}
