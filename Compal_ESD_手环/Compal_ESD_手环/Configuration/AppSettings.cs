namespace Compal_ESD_手环;

internal sealed class AppSettings
{
    public string? CsvPath { get; init; }

    public string? WatchDirectory { get; init; }

    public int? Port { get; init; }

    public bool? ArchiveEnabled { get; init; }

    public string? ArchiveDirectory { get; init; }

    public int? RetainDays { get; init; }

    public int? MaxArchiveSizeMb { get; init; }
}
