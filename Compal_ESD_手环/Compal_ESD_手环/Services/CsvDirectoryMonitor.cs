namespace Compal_ESD_手环;

internal sealed class CsvDirectoryMonitor : IDisposable
{
    private readonly string _watchDirectory;
    private readonly RegisterBank _registerBank;
    private readonly ProcessedFilePolicy _filePolicy;
    private readonly ArchiveRetentionCleaner? _retentionCleaner;
    private readonly FileSystemWatcher _watcher;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _reasonSync = new();
    private readonly Timer _debounceTimer;
    private readonly Dictionary<char, DateTime> _latestProcessedAtByLine = new();
    private bool _disposed;
    private string _pendingReason = "启动扫描";

    public CsvDirectoryMonitor(
        string watchDirectory,
        RegisterBank registerBank,
        ProcessedFilePolicy filePolicy,
        ArchiveRetentionCleaner? retentionCleaner)
    {
        _watchDirectory = watchDirectory;
        _registerBank = registerBank;
        _filePolicy = filePolicy;
        _retentionCleaner = retentionCleaner;
        _watcher = new FileSystemWatcher(_watchDirectory, "*.csv")
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size
        };

        _watcher.Created += OnFileChanged;
        _watcher.Changed += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Renamed += OnFileRenamed;
        _debounceTimer = new Timer(OnDebounceElapsed);
    }

    public async Task InitializeAsync()
    {
        await RefreshAsync("启动扫描");
        _watcher.EnableRaisingEvents = true;
        Console.WriteLine($"已开始监控目录: {_watchDirectory}");
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        ScheduleRefresh($"{e.ChangeType}: {Path.GetFileName(e.FullPath)}");
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        ScheduleRefresh($"Renamed: {Path.GetFileName(e.OldFullPath)} -> {Path.GetFileName(e.FullPath)}");
    }

    private void ScheduleRefresh(string reason)
    {
        if (_disposed)
        {
            return;
        }

        lock (_reasonSync)
        {
            _pendingReason = reason;
        }

        _debounceTimer.Change(500, Timeout.Infinite);
    }

    private void OnDebounceElapsed(object? state)
    {
        _ = Task.Run(async () =>
        {
            string reason;
            lock (_reasonSync)
            {
                reason = _pendingReason;
            }

            await RefreshAsync(reason);
        });
    }

    private async Task RefreshAsync(string reason)
    {
        await _refreshGate.WaitAsync();
        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 触发重载: {reason}");
            foreach (var file in DiscoverPendingFiles())
            {
                try
                {
                    var lineName = file.LineName!.Value;
                    var shouldApply = !_latestProcessedAtByLine.TryGetValue(lineName, out var latestProcessedAt)
                        || file.SortTimestampUtc >= latestProcessedAt;

                    var mapping = await LoadWithRetryAsync(file.Path);
                    if (shouldApply)
                    {
                        _registerBank.ApplyLine(mapping);
                        _latestProcessedAtByLine[lineName] = file.SortTimestampUtc;
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 已加载 {lineName} 线: {Path.GetFileName(file.Path)}，终端状态数: {mapping.LoadedCount}");
                    }
                    else
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 跳过旧文件 {Path.GetFileName(file.Path)}，当前 {lineName} 线已有更新数据");
                    }

                    await FinalizeProcessedFileAsync(file);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 处理文件失败 {Path.GetFileName(file.Path)}: {ex.Message}");
                }
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private List<FileCandidate> DiscoverPendingFiles()
    {
        return Directory
            .GetFiles(_watchDirectory, "*.csv", SearchOption.TopDirectoryOnly)
            .Select(path => new FileCandidate(path))
            .Where(static candidate => candidate.HasLineName)
            .OrderBy(static candidate => candidate.SortTimestampUtc)
            .ThenBy(static candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<LineMappingResult> LoadWithRetryAsync(string csvPath)
    {
        const int maxAttempts = 5;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return CsvStatusLoader.Load(csvPath);
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                await Task.Delay(200);
            }
        }

        return CsvStatusLoader.Load(csvPath);
    }

    private async Task FinalizeProcessedFileAsync(FileCandidate file)
    {
        if (_filePolicy.ArchiveEnabled)
        {
            if (_filePolicy.ArchiveDirectory is null)
            {
                throw new InvalidOperationException("归档目录未配置。");
            }

            await MoveToArchiveAsync(file.Path, _filePolicy.ArchiveDirectory, file.SortTimestampUtc);
            _retentionCleaner?.RequestCleanup();
            return;
        }

        await DeleteFileAsync(file.Path);
    }

    private static async Task MoveToArchiveAsync(string sourcePath, string archiveDirectory, DateTime sortTimestampUtc)
    {
        await RetryFileOperationAsync(() =>
        {
            var localTime = sortTimestampUtc.ToLocalTime();
            var targetDirectory = Path.Combine(
                archiveDirectory,
                localTime.ToString("yyyy-MM-dd"),
                localTime.ToString("HH"));

            Directory.CreateDirectory(targetDirectory);

            var fileName = Path.GetFileName(sourcePath);
            var destinationPath = BuildUniquePath(targetDirectory, fileName);
            File.Move(sourcePath, destinationPath);
        });
    }

    private static async Task DeleteFileAsync(string path)
    {
        await RetryFileOperationAsync(() =>
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        });
    }

    private static async Task RetryFileOperationAsync(Action action)
    {
        const int maxAttempts = 5;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                await Task.Delay(200);
            }
        }

        action();
    }

    private static string BuildUniquePath(string directory, string fileName)
    {
        var destinationPath = Path.Combine(directory, fileName);
        if (!File.Exists(destinationPath))
        {
            return destinationPath;
        }

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var suffix = 1;

        while (true)
        {
            var candidate = Path.Combine(directory, $"{nameWithoutExtension}_{suffix}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }

            suffix++;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnFileChanged;
        _watcher.Changed -= OnFileChanged;
        _watcher.Deleted -= OnFileChanged;
        _watcher.Renamed -= OnFileRenamed;
        _watcher.Dispose();
        _debounceTimer.Dispose();
        _refreshGate.Dispose();
    }

    private sealed record FileCandidate(string Path)
    {
        public bool HasLineName => LineName.HasValue;
        public char? LineName { get; } = ResolveLineName(Path);
        public DateTime LastWriteTimeUtc { get; } = File.GetLastWriteTimeUtc(Path);
        public DateTime SortTimestampUtc { get; } = ResolveSortTimestampUtc(Path);

        private static char? ResolveLineName(string path)
        {
            return CsvStatusLoader.TryResolveLineName(System.IO.Path.GetFileNameWithoutExtension(path), out var lineName)
                ? lineName
                : null;
        }

        private static DateTime ResolveSortTimestampUtc(string path)
        {
            var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
            var timestampText = fileName.Split('_').LastOrDefault();

            if (timestampText is not null
                && DateTime.TryParseExact(
                    timestampText,
                    "yyyyMMddHHmmss",
                    null,
                    System.Globalization.DateTimeStyles.AssumeLocal,
                    out var parsed))
            {
                return parsed.ToUniversalTime();
            }

            return File.GetLastWriteTimeUtc(path);
        }
    }
}
