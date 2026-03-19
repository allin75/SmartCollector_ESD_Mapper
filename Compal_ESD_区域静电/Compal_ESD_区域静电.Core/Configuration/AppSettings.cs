using Compal_ESD_区域静电.Core.Models;

namespace Compal_ESD_区域静电.Core.Configuration;

internal sealed class AppSettings
{
    public int? TcpPort { get; init; }

    public int? PollingIntervalMs { get; init; }

    public List<Mapper>? Mappings { get; init; }
}
