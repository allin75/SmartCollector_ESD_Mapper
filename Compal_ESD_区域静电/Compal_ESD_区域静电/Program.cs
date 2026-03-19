using Compal_ESD_区域静电.Core.Services;
using NLog;

var baseDir = AppContext.BaseDirectory;
Directory.CreateDirectory(Path.Combine(baseDir, "Configs"));
Directory.CreateDirectory(Path.Combine(baseDir, "logs"));

var nlogFile = Path.Combine(baseDir, "NLog.config");
if (File.Exists(nlogFile))
{
    LogManager.Setup().LoadConfigurationFromFile(nlogFile);
}

var logger = LogManager.GetCurrentClassLogger();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

var runOnce = args.Any(a => string.Equals(a, "--once", StringComparison.OrdinalIgnoreCase));
var options = CollectorRuntimeOptions.Load();
using var worker = new DataCollectWorker(options);

logger.Info("Collector started. baseDir={0}, runOnce={1}", baseDir, runOnce);
Console.WriteLine("Compal_ESD_区域静电 is running. Press Ctrl+C to stop.");

try
{
    if (runOnce)
    {
        await worker.RunOnceAsync(cts.Token);
    }
    else
    {
        await worker.RunAsync(cts.Token);
    }
}
catch (OperationCanceledException)
{
    logger.Info("Collector canceled.");
}
catch (Exception ex)
{
    logger.Error(ex, "Collector crashed.");
    Environment.ExitCode = 1;
}
finally
{
    logger.Info("Collector stopped.");
    LogManager.Shutdown();
}
