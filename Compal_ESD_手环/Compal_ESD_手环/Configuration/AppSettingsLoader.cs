using System.Text.Json;

namespace Compal_ESD_手环;

internal static class AppSettingsLoader
{
    private const string FileName = "appsettings.json";

    public static (AppSettings Settings, string BaseDirectory) Load()
    {
        var configPath = ResolveConfigPath();
        if (configPath is null)
        {
            return (new AppSettings(), AppContext.BaseDirectory);
        }

        var json = File.ReadAllText(configPath);
        var settings = JsonSerializer.Deserialize<AppSettings>(
                           json,
                           new JsonSerializerOptions
                           {
                               PropertyNameCaseInsensitive = true
                           })
                       ?? new AppSettings();

        return (settings, Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory);
    }

    private static string? ResolveConfigPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, FileName),
            Path.Combine(Directory.GetCurrentDirectory(), FileName),
            Path.Combine(Directory.GetCurrentDirectory(), "Compal_ESD_手环", FileName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
