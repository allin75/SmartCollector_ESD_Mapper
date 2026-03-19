using System.Text;

namespace Compal_ESD_手环;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            await EsdModbusHost.RunAsync(args);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("服务已停止。");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"启动失败: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }
}
