namespace Compal_ESD_手环;

internal sealed record LineMappingResult(string CsvPath, char LineName, int LoadedCount, ushort[] LineRegisters)
{
    public int LineIndex => char.ToUpperInvariant(LineName) - 'A';
}
