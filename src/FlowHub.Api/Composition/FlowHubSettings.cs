using FlowHub.Api.Database;
using FlowHub.Simulator;
using FlowHub.Traffic;
using FlowHub.Traffic.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace FlowHub.Api.Composition;

/// <summary>
/// 起動に使う設定。appsettings の「Database」「Traffic:Planning」「Traffic:AgvEndpoints」「Simulator」から読む。書いていないものは既定値。
/// </summary>
internal sealed record FlowHubSettings(
    DatabaseOptions Database,
    TrafficPlanningOptions Planning,
    AgvEndpointOptions AgvEndpoints,
    SimulatorOptions Simulator)
{
    public static FlowHubSettings Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new FlowHubSettings(
            configuration.GetSection("Database").Get<DatabaseOptions>() ?? new DatabaseOptions(),
            configuration.GetSection("Traffic:Planning").Get<TrafficPlanningOptions>() ?? new TrafficPlanningOptions(),
            configuration.GetSection("Traffic:AgvEndpoints").Get<AgvEndpointOptions>() ?? new AgvEndpointOptions(),
            configuration.GetSection("Simulator").Get<SimulatorOptions>() ?? new SimulatorOptions());
    }
}
