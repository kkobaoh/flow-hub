using System.Globalization;
using FlowHub.Consumables;
using FlowHub.Consumables.Application;
using FlowHub.Dispatching;
using Microsoft.Extensions.Logging;

namespace FlowHub.Simulator;

/// <summary>
/// シミュレーターが通知を送る先（消耗品の管理）。テストで差し替えられるように、使う窓口だけを切り出す。
/// </summary>
public interface ISimulationTarget
{
    IReadOnlyList<StationId> Stations { get; }

    Task ReportTransferCompletedAsync(TransportId transport, IReadOnlyList<ConsumableId> transferred, CancellationToken cancellationToken);

    Task ReportShippedFromStationAsync(StationId station, ConsumableId id, CancellationToken cancellationToken);

    void RegisterExpectedArrival(ConsumableId id, VarietyId variety);

    Task ReportReadAtStationAsync(StationId station, ConsumableId id, CancellationToken cancellationToken);
}

/// <summary>シミュレーターの通知を、消耗品の管理に渡す。</summary>
public sealed class ConsumablesSimulationTarget(ConsumablesService consumables) : ISimulationTarget
{
    public IReadOnlyList<StationId> Stations => consumables.Stations;

    public Task ReportTransferCompletedAsync(TransportId transport, IReadOnlyList<ConsumableId> transferred, CancellationToken cancellationToken) =>
        consumables.ReportTransferCompletedAsync(transport, transferred, cancellationToken);

    public Task ReportShippedFromStationAsync(StationId station, ConsumableId id, CancellationToken cancellationToken) =>
        consumables.ReportShippedFromStationAsync(station, id, cancellationToken);

    public void RegisterExpectedArrival(ConsumableId id, VarietyId variety) => consumables.RegisterExpectedArrival(id, variety);

    public Task ReportReadAtStationAsync(StationId station, ConsumableId id, CancellationToken cancellationToken) =>
        consumables.ReportReadAtStationAsync(station, id, cancellationToken);
}

/// <summary>
/// 装置・ステーション・上位システムのシミュレーター。本物の通信ができるまで、ポートの仮の実装として使う（docs/plan.md「シミュレーター」）。
/// 頼まれたら、設定の待ち時間のあとに、いつも頼んだとおりに完了を通知する。
/// </summary>
/// <remarks>
/// 通知を送ったあとは、組み立てが渡した後処理（ためたイベントを届ける）を呼ぶ。通知が失敗したら、ログに残して続ける。
/// </remarks>
public sealed partial class EquipmentSimulator(SimulatorOptions options, TimeProvider timeProvider, ILogger<EquipmentSimulator> logger)
    : IDeviceGateway, IStationGateway, IUpstreamGateway
{
    private readonly Lock _lock = new();
    private readonly HashSet<Task> _running = [];
    private readonly Dictionary<VarietyId, int> _serials = [];
    private ISimulationTarget? _target;
    private Func<CancellationToken, Task> _afterNotified = _ => Task.CompletedTask;

    /// <summary>
    /// 通知を送る先と、通知のあとの後処理をつなぐ（消耗品の管理は、このシミュレーターを受け取って作るので、あとからつなぐ）。
    /// </summary>
    public void Attach(ISimulationTarget target, Func<CancellationToken, Task> afterNotified)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(afterNotified);
        _target = target;
        _afterNotified = afterNotified;
    }

    /// <summary>今動いている頼みが、すべて終わるまで待つ（テスト用）。</summary>
    internal Task WhenIdleAsync()
    {
        lock (_lock)
        {
            return Task.WhenAll(_running);
        }
    }

    Task IDeviceGateway.GrantTransferAsync(DeviceId device, TransportId transport, TransferSide side, IReadOnlyList<CargoSlot> cargo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cargo);
        var ids = cargo.Select(c => c.Consumable).ToArray();
        return Later(options.DeviceTransferDelay, (target, token) => target.ReportTransferCompletedAsync(transport, ids, token));
    }

    Task IStationGateway.GrantTransferAsync(StationId station, TransportId transport, TransferSide side, IReadOnlyList<ConsumableId> cargo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cargo);
        var ids = cargo.ToArray();
        return Later(options.StationTransferDelay, (target, token) => target.ReportTransferCompletedAsync(transport, ids, token));
    }

    Task IStationGateway.RequestShipmentAsync(StationId station, ConsumableId consumable, CancellationToken cancellationToken) =>
        Later(options.ShipmentDelay, (target, token) => target.ReportShippedFromStationAsync(station, consumable, token));

    Task IUpstreamGateway.RequestReplenishmentAsync(VarietyId variety, int quantity, CancellationToken cancellationToken)
    {
        // 連番は、頼まれたときに決める（頼まれた順に振る）
        var ids = Enumerable.Range(0, quantity).Select(_ => NextId(variety)).ToArray();
        return Later(options.ReplenishmentDelay, async (target, token) =>
        {
            var station = ReplenishmentStation(target);
            foreach (var id in ids)
            {
                target.RegisterExpectedArrival(id, variety);
                await target.ReportReadAtStationAsync(station, id, token).ConfigureAwait(false);
            }
        });
    }

    Task IUpstreamGateway.NotifySetupErrorAsync(DeviceId device, VarietyId variety, CancellationToken cancellationToken)
    {
        LogSetupError(logger, device.ToString(), variety.ToString());
        return Task.CompletedTask;
    }

    private ConsumableId NextId(VarietyId variety)
    {
        lock (_lock)
        {
            var serial = _serials.GetValueOrDefault(variety) + 1;
            _serials[variety] = serial;
            return new ConsumableId(string.Create(CultureInfo.InvariantCulture, $"SIM-{variety}-{serial}"));
        }
    }

    private StationId ReplenishmentStation(ISimulationTarget target) =>
        options.ReplenishmentStation is { } configured
            ? new StationId(configured)
            : target.Stations.OrderBy(s => s.Value, StringComparer.Ordinal).First();

    /// <summary>待ち時間のあとに通知する。頼んだ側は待たせない。</summary>
    private Task Later(TimeSpan delay, Func<ISimulationTarget, CancellationToken, Task> notify)
    {
        var target = _target ?? throw new InvalidOperationException("シミュレーターの通知先がつながっていません。");
        var running = RunAsync(delay, target, notify);
        lock (_lock)
        {
            if (!running.IsCompleted)
            {
                _running.Add(running);
            }
        }

        _ = running.ContinueWith(
            finished =>
            {
                lock (_lock)
                {
                    _running.Remove(finished);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return Task.CompletedTask;
    }

    private async Task RunAsync(TimeSpan delay, ISimulationTarget target, Func<ISimulationTarget, CancellationToken, Task> notify)
    {
        // 通知の失敗で止まらないよう、例外を投げない待ち方で待ち、失敗はログに残して続ける
        var notifying = NotifyAfterAsync(delay, target, notify);
        await notifying.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (notifying.Exception is { } failure)
        {
            LogNotifyFailed(logger, failure.InnerException ?? failure);
        }
    }

    private async Task NotifyAfterAsync(TimeSpan delay, ISimulationTarget target, Func<ISimulationTarget, CancellationToken, Task> notify)
    {
        await Task.Delay(delay, timeProvider, CancellationToken.None).ConfigureAwait(false);
        await notify(target, CancellationToken.None).ConfigureAwait(false);
        await _afterNotified(CancellationToken.None).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "シミュレーター：上位システムへの段取り替えのエラー（装置 {Device}、品種 {Variety}）を受けました。")]
    private static partial void LogSetupError(ILogger logger, string device, string variety);

    [LoggerMessage(Level = LogLevel.Error, Message = "シミュレーター：通知に失敗しました。")]
    private static partial void LogNotifyFailed(ILogger logger, Exception exception);
}
