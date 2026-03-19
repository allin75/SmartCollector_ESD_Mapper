namespace Compal_ESD_手环;

internal sealed class ArchiveRetentionCleaner : IDisposable
{
    private readonly string _archiveDirectory;
    private readonly int _retainDays;
    private readonly long _maxArchiveSizeBytes;
    private readonly SemaphoreSlim _cleanupGate = new(1, 1);
    private readonly Timer _timer;
    private bool _disposed;

    public ArchiveRetentionCleaner(string archiveDirectory, int retainDays, long maxArchiveSizeBytes)
    {
        _archiveDirectory = archiveDirectory;
        _retainDays = retainDays;
        _maxArchiveSizeBytes = maxArchiveSizeBytes;
        _timer = new Timer(OnTimerElapsed);
    }

    public void Start()
    {
        Directory.CreateDirectory(_archiveDirectory);
        RequestCleanup();
        _timer.Change(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public void RequestCleanup()
    {
        if (_disposed)
        {
            return;
        }

        _ = Task.Run(CleanupAsync);
    }

    private void OnTimerElapsed(object? state)
    {
        RequestCleanup();
    }

    private async Task CleanupAsync()
    {
        if (!await _cleanupGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            if (!Directory.Exists(_archiveDirectory))
            {
                return;
            }

            var files = Directory
                .GetFiles(_archiveDirectory, "*", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderBy(static file => file.LastWriteTimeUtc)
                .ToList();

            DeleteExpiredFiles(files);
            DeleteOversizedFiles(files);
            DeleteEmptyDirectories(_archiveDirectory);
        }
        finally
        {
            _cleanupGate.Release();
        }
    }

    private void DeleteExpiredFiles(List<FileInfo> files)
    {
        if (_retainDays <= 0)
        {
            return;
        }

        var threshold = DateTime.UtcNow.AddDays(-_retainDays);

        foreach (var file in files.ToList())
        {
            if (file.LastWriteTimeUtc >= threshold)
            {
                continue;
            }

            if (TryDelete(file))
            {
                files.Remove(file);
            }
        }
    }

    private void DeleteOversizedFiles(List<FileInfo> files)
    {
        long totalSize = files.Sum(static file => file.Length);

        foreach (var file in files)
        {
            if (totalSize <= _maxArchiveSizeBytes)
            {
                return;
            }

            if (!TryDelete(file))
            {
                continue;
            }

            totalSize -= file.Length;
        }
    }

    private static void DeleteEmptyDirectories(string rootDirectory)
    {
        foreach (var directory in Directory.GetDirectories(rootDirectory, "*", SearchOption.AllDirectories).OrderByDescending(static path => path.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory, false);
            }
        }
    }

    private static bool TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
        _cleanupGate.Dispose();
    }
}
