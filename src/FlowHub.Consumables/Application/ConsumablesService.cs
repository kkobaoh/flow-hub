using FlowHub.Consumables.Domain;
using FlowHub.Dispatching;

namespace FlowHub.Consumables.Application;

/// <summary>
/// 消耗品の、今の様子。
/// </summary>
public sealed record ConsumableView(
    ConsumableId Id,
    VarietyId Variety,
    ProcessTypeId ProcessType,
    ConsumableCondition Condition,
    ConsumableLocation Location);

/// <summary>
/// 受け渡しの完了の通知を受けた結果。通知の ID を正として所在を更新し、食い違いを返す（ログに残し、画面で警告する）。
/// </summary>
/// <param name="Missing">頼んだのに、通知に入っていなかった消耗品。所在は変えていない。</param>
/// <param name="Unexpected">頼んでいないのに、通知に入っていた消耗品。所在は通知のとおりに更新した。</param>
/// <param name="UnknownIds">登録されていない消耗品の ID。</param>
public sealed record TransferReport(
    IReadOnlyList<ConsumableId> Missing,
    IReadOnlyList<ConsumableId> Unexpected,
    IReadOnlyList<ConsumableId> UnknownIds)
{
    public bool HasMismatch => Missing.Count > 0 || Unexpected.Count > 0 || UnknownIds.Count > 0;
}

/// <summary>
/// 消耗品の管理の窓口。消耗品の所在を持ち、装置やステーションとの受け渡しをとりもつ。
/// </summary>
public sealed class ConsumablesService(
    IDispatchingService dispatching,
    IDeviceGateway devices,
    IStationGateway stations) : IDisposable
{
    /// <summary>消耗品の搬送を頼むときの要求元。この要求元の搬送のイベントだけを扱う。</summary>
    public const string Requester = "consumables";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<DeviceId, DeviceLayout> _devices = [];
    private readonly Dictionary<StationId, StationLayout> _stations = [];
    private readonly Dictionary<ProcessTypeId, ProcessTypeSettings> _processTypes = [];
    private readonly Dictionary<ConsumableId, ConsumableView> _consumables = [];
    private readonly Dictionary<TransportId, PendingTransfer> _pending = [];

    public void Dispose() => _gate.Dispose();

    // ---- 登録と参照 ----

    public void RegisterDevice(DeviceLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        Locked(() => Add(_devices, layout.Id, layout, "装置"));
    }

    public void RegisterStation(StationLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        Locked(() => Add(_stations, layout.Id, layout, "ステーション"));
    }

    public void RegisterProcessType(ProcessTypeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Locked(() => Add(_processTypes, settings.Id, settings, "加工の種類"));
    }

    /// <summary>
    /// 消耗品を登録する（ステーションへの入庫や、画面からの手入力）。
    /// </summary>
    public void RegisterConsumable(
        ConsumableId id, VarietyId variety, ProcessTypeId processType, ConsumableCondition condition, ConsumableLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        Locked(() => Add(_consumables, id, new ConsumableView(id, variety, processType, condition, location), "消耗品"));
    }

    public ConsumableView GetConsumable(ConsumableId id) =>
        Locked(() => _consumables.TryGetValue(id, out var consumable)
            ? consumable
            : throw new InvalidOperationException($"消耗品 {id} は登録されていません。"));

    // ---- 受け渡し ----

    /// <summary>
    /// 配車から、搬送の許可を出してほしいと頼まれた。消耗品の搬送なら、装置やステーションに許可を出す。
    /// 装置で降ろすときは、空いているうち番号が一番小さいスロットを入れ先に決めて渡す。
    /// </summary>
    internal async Task OnTransferPermissionRequestedAsync(TransferPermissionRequested requested, CancellationToken cancellationToken)
    {
        if (requested.Requester != Requester)
        {
            return; // ほかのコンテキストが頼んだ搬送
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cargo = requested.CargoRefs.Select(value => new ConsumableId(value)).ToArray();
            var (device, station) = Endpoints.Parse(requested.Endpoint);

            if (device is { } deviceId)
            {
                var slots = requested.Side == TransferSide.Unload
                    ? AllocateSlots(deviceId, cargo)
                    : cargo.Select(id => new CargoSlot(id, SlotOf(id))).ToArray();

                _pending[requested.Transport] = new PendingTransfer(deviceId, null, requested.Side, cargo, slots.ToDictionary(s => s.Consumable, s => s.Slot));
                await devices.GrantTransferAsync(deviceId, requested.Transport, requested.Side, slots, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var stationId = station!.Value;
                _pending[requested.Transport] = new PendingTransfer(null, stationId, requested.Side, cargo, []);
                await stations.GrantTransferAsync(stationId, requested.Transport, requested.Side, cargo, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 装置やステーションから、受け渡しの完了の通知が来た。通知の ID を正として所在を更新し、配車に伝える。
    /// </summary>
    /// <param name="transferred">出し入れした消耗品の ID。</param>
    public async Task<TransferReport> ReportTransferCompletedAsync(
        TransportId transport, IReadOnlyList<ConsumableId> transferred, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transferred);

        TransferReport report;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_pending.Remove(transport, out var pending))
            {
                throw new InvalidOperationException($"搬送 {transport} には、受け渡しの許可を出していません。");
            }

            var unknown = transferred.Where(id => !_consumables.ContainsKey(id)).ToArray();
            foreach (var id in transferred.Except(unknown))
            {
                _consumables[id] = _consumables[id] with { Location = LocationAfter(pending, transport, id) };
            }

            report = new TransferReport(
                Missing: pending.Expected.Except(transferred).ToArray(),
                Unexpected: transferred.Except(unknown).Except(pending.Expected).ToArray(),
                UnknownIds: unknown);
        }
        finally
        {
            _gate.Release();
        }

        await dispatching.ReportTransferCompletedAsync(transport, cancellationToken).ConfigureAwait(false);
        return report;
    }

    // ---- 内部 ----

    private static ConsumableLocation LocationAfter(PendingTransfer pending, TransportId transport, ConsumableId id) =>
        pending.Side == TransferSide.Load
            ? new OnAgv(transport)
            : pending.Device is { } device
                ? new AtTransferUnit(device, pending.Slots.TryGetValue(id, out var slot) ? slot : null)
                : new AtStation(pending.Station!.Value);

    /// <summary>
    /// 入れ先のスロットを、空いているうち番号が一番小さいものから決める。消耗品があるスロットと、入れ先に決めたスロットは使わない。
    /// </summary>
    private CargoSlot[] AllocateSlots(DeviceId device, ConsumableId[] cargo)
    {
        var layout = _devices.TryGetValue(device, out var found)
            ? found
            : throw new InvalidOperationException($"装置 {device} は登録されていません。");

        var taken = _consumables.Values
            .Select(c => c.Location switch
            {
                InStocker stocker when stocker.Device == device => stocker.Slot,
                AtTransferUnit unit when unit.Device == device => unit.PlannedSlot,
                _ => null,
            })
            .OfType<SlotRef>()
            .Concat(_pending.Values.Where(p => p.Device == device && p.Side == TransferSide.Unload).SelectMany(p => p.Slots.Values))
            .ToHashSet();

        var free = layout.AllSlots.Where(slot => !taken.Contains(slot)).Order().ToArray();
        if (free.Length < cargo.Length)
        {
            throw new InvalidOperationException($"装置 {device} のストッカーに、空いているスロットが足りません。");
        }

        return cargo.Select((id, index) => new CargoSlot(id, free[index])).ToArray();
    }

    private SlotRef SlotOf(ConsumableId id) =>
        _consumables.TryGetValue(id, out var consumable) && consumable.Location is InStocker stocker
            ? stocker.Slot
            : throw new InvalidOperationException($"消耗品 {id} はストッカーにありません。");

    private static void Add<TKey, TValue>(Dictionary<TKey, TValue> items, TKey key, TValue value, string kind)
        where TKey : notnull
    {
        if (!items.TryAdd(key, value))
        {
            throw new InvalidOperationException($"{kind} {key} はすでに登録されています。");
        }
    }

    private void Locked(Action action) => Locked(() =>
    {
        action();
        return true;
    });

    private T Locked<T>(Func<T> action)
    {
        _gate.Wait();
        try
        {
            return action();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 許可を出して、完了の通知を待っている受け渡し。
    /// </summary>
    private sealed record PendingTransfer(
        DeviceId? Device,
        StationId? Station,
        TransferSide Side,
        IReadOnlyList<ConsumableId> Expected,
        Dictionary<ConsumableId, SlotRef> Slots);
}
