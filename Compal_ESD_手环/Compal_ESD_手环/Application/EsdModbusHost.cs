namespace Compal_ESD_手环;

internal static class EsdModbusHost
{
    public static async Task RunAsync(string[] args)
    {
        var options = StartupOptions.Parse(args);
        var registerBank = new RegisterBank();
        using var retentionCleaner = CreateRetentionCleaner(options);

        using var source = await InitializeDataSourceAsync(options, registerBank, retentionCleaner);

        PrintStartupSummary(options);

        using var server = new ModbusTcpServer(options.Port, registerBank);
        await server.StartAsync();
    }

    private static ArchiveRetentionCleaner? CreateRetentionCleaner(StartupOptions options)
    {
        if (options.WatchDirectory is null || !options.ArchiveEnabled || options.ArchiveDirectory is null)
        {
            return null;
        }

        var cleaner = new ArchiveRetentionCleaner(options.ArchiveDirectory, options.RetainDays, options.MaxArchiveSizeBytes);
        cleaner.Start();
        return cleaner;
    }

    private static async Task<IDisposable?> InitializeDataSourceAsync(
        StartupOptions options,
        RegisterBank registerBank,
        ArchiveRetentionCleaner? retentionCleaner)
    {
        if (options.WatchDirectory is not null)
        {
            var filePolicy = ProcessedFilePolicy.FromOptions(options);
            var monitor = new CsvDirectoryMonitor(options.WatchDirectory, registerBank, filePolicy, retentionCleaner);
            await monitor.InitializeAsync();
            return monitor;
        }

        if (options.CsvPath is null)
        {
            throw new InvalidOperationException("未找到可加载的 CSV 文件。");
        }

        var mapping = CsvStatusLoader.Load(options.CsvPath);
        registerBank.ApplyLine(mapping);
        Console.WriteLine($"已加载线别: {mapping.LineName}，来源文件: {mapping.CsvPath}，终端状态数: {mapping.LoadedCount}");
        return null;
    }

    private static void PrintStartupSummary(StartupOptions options)
    {
        Console.WriteLine($"监听地址: 0.0.0.0:{options.Port}");
        Console.WriteLine($"监控模式: {(options.WatchDirectory is null ? "单文件" : "目录持续监控")}");

        if (options.WatchDirectory is null)
        {
            Console.WriteLine($"CSV 文件: {options.CsvPath}");
        }
        else
        {
            Console.WriteLine($"监控目录: {options.WatchDirectory}");
            Console.WriteLine($"处理后文件: {(options.ArchiveEnabled ? $"归档到 {options.ArchiveDirectory}" : "直接删除")}");
            if (options.ArchiveEnabled)
            {
                Console.WriteLine($"归档保留: {options.RetainDays} 天, 最大 {options.MaxArchiveSizeBytes / 1024 / 1024} MB");
            }
        }

        Console.WriteLine("线别寄存器分配:");
        for (var lineName = 'A'; lineName <= 'I'; lineName++)
        {
            var baseOffset = RegisterLayout.GetBaseOffset(lineName);
            Console.WriteLine($"  {lineName} 线 -> {baseOffset}-{baseOffset + RegisterLayout.RegistersPerLine - 1} (保持寄存器 {40001 + baseOffset}-{40001 + baseOffset + RegisterLayout.RegistersPerLine - 1})");
        }

        Console.WriteLine("已支持功能码: 03(Read Holding Registers), 04(Read Input Registers)");
        Console.WriteLine("按 Ctrl+C 停止服务。");
    }
}
