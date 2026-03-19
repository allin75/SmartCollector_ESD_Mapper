namespace Compal_ESD_手环;

internal sealed record StartupOptions(
    string? CsvPath,
    string? WatchDirectory,
    int Port,
    bool ArchiveEnabled,
    string? ArchiveDirectory,
    int RetainDays,
    long MaxArchiveSizeBytes)
{
    private const int DefaultPort = 1502;
    private const int DefaultRetainDays = 3;
    private const int DefaultMaxArchiveSizeMb = 2048;

    public static StartupOptions Parse(string[] args)
    {
        var (appSettings, configBaseDirectory) = AppSettingsLoader.Load();
        string? csvPath = ResolveOptionalPath(appSettings.CsvPath, configBaseDirectory);
        string? watchDirectory = ResolveOptionalPath(appSettings.WatchDirectory, configBaseDirectory);
        string? archiveDirectory = ResolveOptionalPath(appSettings.ArchiveDirectory, configBaseDirectory);
        var port = appSettings.Port ?? DefaultPort;
        var archiveEnabled = appSettings.ArchiveEnabled ?? true;
        var retainDays = appSettings.RetainDays ?? DefaultRetainDays;
        var maxArchiveSizeMb = appSettings.MaxArchiveSizeMb ?? DefaultMaxArchiveSizeMb;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--csv":
                case "-c":
                    EnsureHasNextValue(args, i);
                    csvPath = Path.GetFullPath(args[++i]);
                    break;
                case "--watch-dir":
                case "-d":
                    EnsureHasNextValue(args, i);
                    watchDirectory = Path.GetFullPath(args[++i]);
                    break;
                case "--port":
                case "-p":
                    EnsureHasNextValue(args, i);
                    if (!int.TryParse(args[++i], out port) || port is < 1 or > 65535)
                    {
                        throw new InvalidOperationException("端口必须是 1 到 65535 之间的整数。");
                    }
                    break;
                case "--archive-dir":
                    EnsureHasNextValue(args, i);
                    archiveDirectory = Path.GetFullPath(args[++i]);
                    break;
                case "--retain-days":
                    EnsureHasNextValue(args, i);
                    if (!int.TryParse(args[++i], out retainDays) || retainDays < 0)
                    {
                        throw new InvalidOperationException("归档保留天数必须是大于等于 0 的整数。");
                    }
                    break;
                case "--max-archive-size-mb":
                    EnsureHasNextValue(args, i);
                    if (!int.TryParse(args[++i], out maxArchiveSizeMb) || maxArchiveSizeMb <= 0)
                    {
                        throw new InvalidOperationException("归档大小上限必须是大于 0 的整数(MB)。");
                    }
                    break;
                case "--no-archive":
                    archiveEnabled = false;
                    break;
                case "--help":
                case "-h":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
                default:
                    throw new InvalidOperationException($"未知参数: {args[i]}");
            }
        }

        if (!string.IsNullOrWhiteSpace(watchDirectory))
        {
            if (!Directory.Exists(watchDirectory))
            {
                throw new DirectoryNotFoundException($"监控目录不存在: {watchDirectory}");
            }

            var resolvedArchiveDirectory = archiveEnabled
                ? ResolveArchiveDirectory(watchDirectory, archiveDirectory)
                : null;

            return new StartupOptions(
                null,
                watchDirectory,
                port,
                archiveEnabled,
                resolvedArchiveDirectory,
                retainDays,
                maxArchiveSizeMb * 1024L * 1024L);
        }

        var resolvedCsv = ResolveCsvPath(csvPath);
        return new StartupOptions(resolvedCsv, null, port, false, null, retainDays, maxArchiveSizeMb * 1024L * 1024L);
    }

    private static string? ResolveOptionalPath(string? path, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    private static void EnsureHasNextValue(string[] args, int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new InvalidOperationException($"参数 {args[index]} 缺少值。");
        }
    }

    private static string ResolveCsvPath(string? csvPath)
    {
        if (!string.IsNullOrWhiteSpace(csvPath))
        {
            if (!File.Exists(csvPath))
            {
                throw new FileNotFoundException("指定的 CSV 文件不存在。", csvPath);
            }

            return csvPath;
        }

        var searchRoots = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
            Directory.GetParent(AppContext.BaseDirectory)?.FullName,
            Directory.GetParent(Directory.GetCurrentDirectory())?.FullName
        };

        var candidate = searchRoots
            .Where(static path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .SelectMany(static path => Directory.GetFiles(path!, "*.csv", SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (candidate is null)
        {
            throw new InvalidOperationException("未找到 CSV 文件，请通过 --csv 指定文件路径，或通过 --watch-dir 指定监控目录。");
        }

        return Path.GetFullPath(candidate);
    }

    private static string ResolveArchiveDirectory(string watchDirectory, string? archiveDirectory)
    {
        var resolvedArchiveDirectory = string.IsNullOrWhiteSpace(archiveDirectory)
            ? Path.Combine(watchDirectory, "archive")
            : archiveDirectory;

        if (Path.GetFullPath(resolvedArchiveDirectory).Equals(Path.GetFullPath(watchDirectory), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("归档目录不能与监控目录相同。");
        }

        Directory.CreateDirectory(resolvedArchiveDirectory);
        return resolvedArchiveDirectory;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("用法:");
        Console.WriteLine("  dotnet run --project Compal_ESD_手环.csproj -- --csv <path> [--port <port>]");
        Console.WriteLine("  dotnet run --project Compal_ESD_手环.csproj -- --watch-dir <directory> [--port <port>] [--archive-dir <directory>] [--retain-days <days>] [--max-archive-size-mb <mb>] [--no-archive]");
        Console.WriteLine();
        Console.WriteLine("参数:");
        Console.WriteLine("  --csv,       -c   指定单个 esdbox 导出的 CSV 文件");
        Console.WriteLine("  --watch-dir, -d   指定需要持续监控的 CSV 目录");
        Console.WriteLine("  --port,      -p   指定 Modbus TCP 监听端口，默认 1502");
        Console.WriteLine("  --archive-dir     指定归档目录，默认 <watch-dir>/archive");
        Console.WriteLine("  --retain-days     归档保留天数，默认 3");
        Console.WriteLine("  --max-archive-size-mb 归档总大小上限(MB)，默认 2048");
        Console.WriteLine("  --no-archive      处理成功后直接删除文件，不做归档");
        Console.WriteLine();
        Console.WriteLine("也可直接编辑 appsettings.json，命令行参数优先级更高。");
    }
}
