using FlowHub.Api.Database;
using FlowHub.Map;
using FlowHub.Notifications;
using FlowHub.Simulator;
using FlowHub.Traffic;
using FlowHub.Traffic.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace FlowHub.Api.Composition;

/// <summary>
/// 起動に使う設定。appsettings の「Database」「Traffic:Planning」「Traffic:AgvEndpoints」「Simulator」「InitialData:Path」「System:StartOnLaunch」「Notifications:Capacity」「Map:MinimumCrossDistanceMm」から読む。
/// 書いていないものは既定値。
/// </summary>
/// <param name="InitialDataPath">開発用の初期データのファイル。指定しなければ、初期データは入れない。</param>
/// <param name="StartOnLaunch">起動したらすぐ、運転状態をスタートにするか（開発用。指定しなければ停止から始める）。</param>
/// <param name="NoticeCapacity">画面で知らせるメッセージを、新しいほうから何件残すか。</param>
/// <param name="MinimumCrossDistanceMm">マップの検証で、隣り合うパネルの十字の交点の間に求める最小の距離（mm）。</param>
internal sealed record FlowHubSettings(
    DatabaseOptions Database,
    TrafficPlanningOptions Planning,
    AgvEndpointOptions AgvEndpoints,
    SimulatorOptions Simulator,
    string? InitialDataPath = null,
    bool StartOnLaunch = false,
    int NoticeCapacity = InMemoryNoticeBoard.DefaultCapacity,
    int MinimumCrossDistanceMm = MapValidationOptions.DefaultMinimumCrossDistanceMm)
{
    /// <param name="contentRoot">アプリのフォルダー。初期データのファイルの場所は、ここからの場所として読む。</param>
    public static FlowHubSettings Read(IConfiguration configuration, string contentRoot)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var initialData = configuration["InitialData:Path"];
        return new FlowHubSettings(
            configuration.GetSection("Database").Get<DatabaseOptions>() ?? new DatabaseOptions(),
            configuration.GetSection("Traffic:Planning").Get<TrafficPlanningOptions>() ?? new TrafficPlanningOptions(),
            configuration.GetSection("Traffic:AgvEndpoints").Get<AgvEndpointOptions>() ?? new AgvEndpointOptions(),
            configuration.GetSection("Simulator").Get<SimulatorOptions>() ?? new SimulatorOptions(),
            string.IsNullOrWhiteSpace(initialData) ? null : Path.Combine(contentRoot, initialData),
            configuration.GetValue<bool>("System:StartOnLaunch"),
            configuration.GetValue("Notifications:Capacity", InMemoryNoticeBoard.DefaultCapacity),
            configuration.GetValue("Map:MinimumCrossDistanceMm", MapValidationOptions.DefaultMinimumCrossDistanceMm));
    }

    /// <summary>マップを公開するときの検証のオプション。</summary>
    public MapValidationOptions MapValidation => new() { MinimumCrossDistanceMm = MinimumCrossDistanceMm };
}
