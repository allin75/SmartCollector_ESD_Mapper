using System.Text;
using System.Text.RegularExpressions;

namespace Compal_ESD_手环;

internal static class CsvStatusLoader
{
    private const string Delimiter = "--";
    private static readonly Regex AssyLineRegex = new(@"ASSY([A-I])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static LineMappingResult Load(string csvPath)
    {
        var fullPath = Path.GetFullPath(csvPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("CSV 文件不存在。", fullPath);
        }

        var fileName = Path.GetFileNameWithoutExtension(fullPath);
        var lineName = ResolveLineName(fileName);
        var lineRegisters = new ushort[RegisterLayout.RegistersPerLine];
        var loadedCount = 0;

        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        while (reader.ReadLine() is { } rawLine)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(Delimiter, StringSplitOptions.None);
            if (parts.Length < 5)
            {
                continue;
            }

            if (!ushort.TryParse(parts[^1].Trim(), out var terminalStatus))
            {
                continue;
            }

            if (loadedCount >= RegisterLayout.RegistersPerLine)
            {
                throw new InvalidOperationException($"线别 {lineName} 的数据超过 {RegisterLayout.RegistersPerLine} 条，无法映射。");
            }

            lineRegisters[loadedCount] = terminalStatus;
            loadedCount++;
        }

        return new LineMappingResult(fullPath, lineName, loadedCount, lineRegisters);
    }

    public static bool TryResolveLineName(string fileName, out char lineName)
    {
        var normalized = fileName.Trim().ToUpperInvariant();
        var assyMatch = AssyLineRegex.Match(normalized);
        if (assyMatch.Success)
        {
            lineName = assyMatch.Groups[1].Value[0];
            return true;
        }

        if (normalized.Length > 0 && normalized[0] is >= 'A' and <= 'I')
        {
            lineName = normalized[0];
            return true;
        }

        foreach (var token in normalized.Split(['_', '-', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length == 1 && token[0] is >= 'A' and <= 'I')
            {
                lineName = token[0];
                return true;
            }
        }

        lineName = default;
        return false;
    }

    private static char ResolveLineName(string fileName)
    {
        if (TryResolveLineName(fileName, out var lineName))
        {
            return lineName;
        }

        throw new InvalidOperationException($"无法从文件名 \"{fileName}\" 中识别 A 到 I 线。");
    }
}
