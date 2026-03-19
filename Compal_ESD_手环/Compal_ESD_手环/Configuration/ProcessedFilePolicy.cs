namespace Compal_ESD_手环;

internal sealed record ProcessedFilePolicy(bool ArchiveEnabled, string? ArchiveDirectory)
{
    public static ProcessedFilePolicy FromOptions(StartupOptions options)
    {
        return new ProcessedFilePolicy(options.ArchiveEnabled, options.ArchiveDirectory);
    }
}
