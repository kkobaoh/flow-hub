using FlowHub.Consumables.Application;
using FlowHub.Dispatching.Application;
using FlowHub.Map;
using FlowHub.Map.Infrastructure;
using FlowHub.Notifications;
using FlowHub.Traffic;
using FlowHub.Traffic.Application;
using Microsoft.Extensions.Logging;

namespace FlowHub.Api.Composition;

/// <summary>マップの下書きを公開した結果。</summary>
internal enum MapPublishResult
{
    /// <summary>公開し、すぐにその版に切り替えた。下書きは消した。</summary>
    Published,

    /// <summary>読んだあとに、ほかの人が下書きを保存したか公開した（公開していない）。</summary>
    Stale,

    /// <summary>運転状態がスタート（公開していない）。</summary>
    Running,

    /// <summary>走っている AGV がある（公開していない）。</summary>
    AgvMoving,

    /// <summary>検証で問題が見つかった（公開していない）。装置やステーションの受け渡しパネルがない版も、ここに入る。</summary>
    Invalid,
}

/// <summary>公開した結果と、その中身。</summary>
/// <param name="Version">公開した版の番号（公開したときだけ）。</param>
/// <param name="Problems">検証で見つかった問題（<see cref="MapPublishResult.Invalid"/> のとき）。</param>
/// <param name="Agvs">走っている AGV（<see cref="MapPublishResult.AgvMoving"/> のとき）か、切り離しにした AGV（公開したとき）。</param>
internal sealed record MapPublishOutcome(MapPublishResult Result, int Version, IReadOnlyList<string> Problems, IReadOnlyList<string> Agvs);

/// <summary>
/// マップの下書きを検証し、公開する（組み立て）。マップ、交通整理、配車、消耗品の管理にまたがるので、組み立てで行う。
/// </summary>
/// <remarks>
/// 公開できるのは、運転状態が停止で、走っている AGV がないときだけ。公開したら、すぐにその版に切り替え、下書きを消す。
/// 装置やステーションの受け渡しパネルがない版は、検証の問題として断る。いるパネルが新しい版にない AGV は、公開する前に切り離しにする
/// （公開に失敗しても、切り離しのまま。動かさない側に倒す）。
/// 公開は1つずつ行う。運転状態のスタートとは同じロックで守っていないので、確かめてから公開するまでのごく短い間にスタートされることはありうる。
/// </remarks>
internal sealed partial class MapPublisher(
    DbMapStore map,
    MapValidationOptions validation,
    TrafficService traffic,
    DispatchingService dispatching,
    ConsumablesService consumables,
    INoticeBoard notices,
    ILogger<MapPublisher> logger) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public void Dispose() => _gate.Dispose();

    /// <summary>下書きを、公開するときと同じに検証し、見つかった問題を返す（装置やステーションの受け渡しパネルも確かめる）。</summary>
    public IReadOnlyList<string> Validate(MapDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var result = draft.ToSnapshot(validation);
        return result.Snapshot is { } snapshot ? EquipmentProblems(snapshot) : [.. result.Errors.Select(e => e.Message)];
    }

    /// <summary>下書きを公開する。</summary>
    /// <param name="revision">読んだときの下書きの番号。</param>
    public async Task<MapPublishOutcome> PublishAsync(int revision, MapDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (map.ReadDraft().Revision != revision)
            {
                return Refused(MapPublishResult.Stale);
            }

            if (dispatching.IsRunning)
            {
                return Refused(MapPublishResult.Running);
            }

            var moving = traffic.ListAgvs().Where(a => a.State == AgvState.Moving || a.CarrierMoving).Select(a => a.Id.Value).Order(StringComparer.Ordinal).ToArray();
            if (moving.Length > 0)
            {
                return new MapPublishOutcome(MapPublishResult.AgvMoving, 0, [], moving);
            }

            var result = draft.ToSnapshot(validation);
            if (result.Snapshot is not { } snapshot)
            {
                return new MapPublishOutcome(MapPublishResult.Invalid, 0, [.. result.Errors.Select(e => e.Message)], []);
            }

            var problems = EquipmentProblems(snapshot);
            if (problems.Length > 0)
            {
                return new MapPublishOutcome(MapPublishResult.Invalid, 0, problems, []);
            }

            // 先に切り離しにしてから公開する（公開したあとに、なくなったパネルにいる AGV が残らないように）
            var detached = await traffic.DetachAgvsOffMapAsync(snapshot.Panels.Select(p => p.Id).ToHashSet(), cancellationToken).ConfigureAwait(false);
            map.Publish(snapshot);
            map.DeleteDraft();
            await traffic.MapPublishedAsync(cancellationToken).ConfigureAwait(false);

            var version = map.CurrentVersion;
            var names = detached.Select(a => a.Value).ToArray();
            LogPublished(logger, version, names.Length == 0 ? "なし" : string.Join("、", names));
            if (names.Length > 0)
            {
                await notices.PostAsync(
                    $"マップの版{version}を公開し、{string.Join("、", names)} を切り離しにしました",
                    "いるパネルが新しい版にないため、位置がわからなくなりました。位置を指定して、切り離しから戻してください。",
                    cancellationToken).ConfigureAwait(false);
            }

            return new MapPublishOutcome(MapPublishResult.Published, version, [], names);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>登録してある装置とステーションの受け渡しパネルが、新しい版の受け渡しパネル（特殊消耗品受け渡しパネルを含む）か。</summary>
    private string[] EquipmentProblems(MapSnapshot snapshot)
    {
        var transfer = snapshot.Panels.Where(p => p.Type is PanelType.Transfer or PanelType.SpecialTransfer).Select(p => p.Id).ToHashSet();
        var devices = consumables.Devices.Select(d => (Kind: "装置", Id: d.Value, consumables.DeviceEndpoint(d).Panel));
        var stations = consumables.Stations.Select(s => (Kind: "ステーション", Id: s.Value, consumables.StationEndpoint(s).Panel));
        return [.. devices.Concat(stations)
            .Where(e => !transfer.Contains(e.Panel))
            .Select(e => $"{e.Kind} {e.Id} の受け渡しパネル {e.Panel} が、この版にないか、受け渡しパネルではありません。消耗品の構成で受け渡しパネルを変えるか、{e.Kind}を消してください。")];
    }

    private static MapPublishOutcome Refused(MapPublishResult result) => new(result, 0, [], []);

    [LoggerMessage(Level = LogLevel.Information, Message = "マップの版{Version}を公開しました。切り離しにした AGV：{Detached}")]
    private static partial void LogPublished(ILogger logger, int version, string detached);
}
