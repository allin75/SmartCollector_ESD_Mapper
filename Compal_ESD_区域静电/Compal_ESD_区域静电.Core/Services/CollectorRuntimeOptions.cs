using Compal_ESD_区域静电.Core.Configuration;
using Compal_ESD_区域静电.Core.Models;

namespace Compal_ESD_区域静电.Core.Services;

public sealed class CollectorRuntimeOptions
{
    private const int DefaultTcpPort = 502;
    private const int DefaultPollingIntervalMs = 1000;

    public int TcpPort { get; init; } = DefaultTcpPort;

    public int PollingIntervalMs { get; init; } = DefaultPollingIntervalMs;

    public IReadOnlyList<Mapper> Mappings { get; init; } = Array.Empty<Mapper>();

    public static CollectorRuntimeOptions Load()
    {
        var appSettings = AppSettingsLoader.Load();
        var mappings = NormalizeMappings(appSettings.Mappings);

        return new CollectorRuntimeOptions
        {
            TcpPort = appSettings.TcpPort ?? DefaultTcpPort,
            PollingIntervalMs = appSettings.PollingIntervalMs ?? DefaultPollingIntervalMs,
            Mappings = mappings
        };
    }

    private static IReadOnlyList<Mapper> NormalizeMappings(List<Mapper>? mappings)
    {
        if (mappings is null || mappings.Count == 0)
        {
            return Array.Empty<Mapper>();
        }

        return mappings
            .Where(static mapping => !string.IsNullOrWhiteSpace(mapping.PortName))
            .Select(static mapping => new Mapper
            {
                Id = mapping.Id,
                Name = mapping.Name,
                PortName = mapping.PortName.Trim(),
                BaudRate = mapping.BaudRate <= 0 ? 9600 : mapping.BaudRate,
                ReadType = mapping.ReadType,
                SlaveId = mapping.SlaveId == 0 ? (byte)1 : mapping.SlaveId,
                SourceRegisterAddress = mapping.SourceRegisterAddress,
                TargetRegisterOffset = mapping.TargetRegisterOffset,
                Value = mapping.Value
            })
            .ToArray();
    }
}
