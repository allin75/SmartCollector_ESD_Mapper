using System.Text.Json;

namespace Compal_ESD_区域静电.Core.Configuration;

internal static class AppSettingsLoader
{
    private const string FileName = "appsettings.json";

    public static AppSettings Load()
    {
        var configPath = ResolveConfigPath();
        if (configPath is null)
        {
            return new AppSettings();
        }

        var json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize<AppSettings>(
                   json,
                   new JsonSerializerOptions
                   {
                       PropertyNameCaseInsensitive = true
                   })
               ?? new AppSettings();
    }

    private static string? ResolveConfigPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, FileName),
            Path.Combine(Directory.GetCurrentDirectory(), FileName),
            Path.Combine(Directory.GetCurrentDirectory(), "Compal_ESD_区域静电", FileName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
