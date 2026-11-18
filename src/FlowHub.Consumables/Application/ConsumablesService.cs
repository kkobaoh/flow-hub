using FlowHub.BuildingBlocks;
using FlowHub.Consumables.Domain;
using FlowHub.Dispatching;

namespace FlowHub.Consumables.Application;

/// <summary>
/// 消耗品の、今の様子。
/// </summary>
/// <param name="Variety">品種。未登録の ID を仮に登録したもの（照合のエラーなど）は、わからないので null。</param>
/// <param name="ProcessType">加工の種類。ステーションで読み取った未登録の ID など、わからなければ null。</param>
/// <param name="NeedsCheck">記録と現物が合わず、人の確認が要る（ステーションで届ける候補にしない）。</param>
public sealed record ConsumableView(
    ConsumableId Id,
    VarietyId? Variety,
    ProcessTypeId? ProcessType,
    ConsumableCondition Condition,
    ConsumableLocation Location,
    bool NeedsCheck = false);

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
/// <remarks>
/// 搬送計画のために、不足と回収の判断、ステーションの在庫、届ける・回収する予約、補充の頼みの口も持つ。
/// 判断にかかわる在庫が変わったら <see cref="StockChanged"/> で知らせる（予約は搬送計画自身が行うので知らせない）。
/// </remarks>
public sealed partial class ConsumablesService(
    IDispatchingService dispatching,
    IDeviceGateway devices,
    IStationGateway stations,
    IUpstreamGateway upstream,
    IEventPublisher publisher,
    TimeProvider timeProvider) : IDisposable
{
    /// <summary>消耗品の搬送を頼むときの要求元。この要求元の搬送のイベントだけを扱う。</summary>
    public const string Requester = "consumables";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<DeviceId, DeviceLayout> _devices = [];
    private readonly Dictionary<StationId, StationLayout> _stations = [];
    private readonly Dictionary<ProcessTypeId, ProcessTypeSettings> _processTypes = [];
    private readonly Dictionary<VarietyId, ProcessTypeId> _varieties = [];
    private readonly Dictionary<DeviceId, IReadOnlyDictionary<VarietyId, int>> _settings = [];
    private readonly Dictionary<ConsumableId, ConsumableView> _consumables = [];
    private readonly Dictionary<TransportId, PendingTransfer> _pending = [];

    /// <summary>ステーションに入庫した順の番号（先入れ先出しの仮の基準）。</summary>
    private readonly Dictionary<ConsumableId, long> _arrivalOrder = [];
    private long _nextArrival = 1;

    /// <summary>届ける予約（消耗品 → 届け先の装置）。装置で降ろしたら外す。</summary>
    private readonly Dictionary<ConsumableId, DeviceId> _deliveries = [];

    /// <summary>回収の予約。装置から積み込んだら外す。</summary>
    private readonly HashSet<ConsumableId> _recoveries = [];

    /// <summary>廃棄リクエストを受けた消耗品と装置。装置から積み込んだら外す。</summary>
    private readonly Dictionary<ConsumableId, DeviceId> _discards = [];

    /// <summary>品種ごとの、補充を頼んで、まだ入庫されていない数。</summary>
    private readonly Dictionary<VarietyId, int> _outstanding = [];

    /// <summary>前もって登録された、入庫する予定の ID と品種の対応。</summary>
    private readonly Dictionary<ConsumableId, VarietyId> _expected = [];

    /// <summary>未登録の ID として入庫した（品種の対応がなかった）消耗品。新品と直したら、補充の入庫に数える。</summary>
    private readonly HashSet<ConsumableId> _arrivedUnregistered = [];

    /// <summary>出庫を頼んで、まだ出ていない消耗品（届ける候補にしない）。</summary>
    private readonly HashSet<ConsumableId> _shipping = [];

    public void Dispose() => _gate.Dispose();

    // ---- 登録と参照 ----

    /// <summary>
    /// 品種と、その品種が属する加工の種類を登録する。
    /// </summary>
    public void RegisterVariety(VarietyId variety, ProcessTypeId processType) =>
        Locked(() =>
        {
            ProcessType(processType);
            Add(_varieties, variety, processType, "品種");
        });

    /// <summary>登録されている装置（登録した順）。</summary>
    public IReadOnlyList<DeviceId> Devices => Locked(() => _devices.Keys.ToArray());

    /// <summary>登録されているステーション（登録した順）。</summary>
    public IReadOnlyList<StationId> Stations => Locked(() => _stations.Keys.ToArray());

    public TransferEndpoint DeviceEndpoint(DeviceId device) =>
        Locked(() => new TransferEndpoint(Layout(device).TransferPanel, Endpoints.Device(device)));

    public TransferEndpoint StationEndpoint(StationId station) =>
        Locked(() => new TransferEndpoint(
            _stations.TryGetValue(station, out var layout) ? layout.TransferPanel : throw new InvalidOperationException($"ステーション {station} は登録されていません。"),
            Endpoints.Station(station)));

    /// <summary>品種が属する加工の種類の設定。</summary>
    public ProcessTypeSettings ProcessTypeOf(VarietyId variety) => Locked(() => ProcessType(VarietyProcessType(variety)));

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
        Locked(() =>
        {
            Add(_consumables, id, new ConsumableView(id, variety, processType, condition, location), "消耗品");
            NoteArrival(id, location);
        });
    }

    public ConsumableView GetConsumable(ConsumableId id) => Locked(() => Consumable(id));

    // ---- 不足と回収の判断 ----

    /// <summary>
    /// 装置のストッカーの設定（品種ごとにストックしておく個数）を変える。
    /// </summary>
    public async Task SetStockerSettingAsync(DeviceId device, IReadOnlyDictionary<VarietyId, int> counts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(counts);

        await LockedAsync(() =>
        {
            Layout(device);
            foreach (var (variety, count) in counts)
            {
                VarietyProcessType(variety);
                ArgumentOutOfRangeException.ThrowIfNegative(count, nameof(counts));
            }

            _settings[device] = new Dictionary<VarietyId, int>(counts);
            return Task.CompletedTask;
        }, cancellationToken).ConfigureAwait(false);

        await PublishStockChangedAsync($"device:{device}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 装置から、中古品の廃棄リクエストが来た。
    /// </summary>
    public async Task RequestDiscardAsync(DeviceId device, ConsumableId id, CancellationToken cancellationToken)
    {
        await LockedAsync(() =>
        {
            if (Consumable(id).Location is not InStocker stocker || stocker.Device != device)
            {
                throw new InvalidOperationException($"消耗品 {id} は装置 {device} のストッカーにありません。");
            }

            _discards[id] = device;
            return Task.CompletedTask;
        }, cancellationToken).ConfigureAwait(false);

        await PublishStockChangedAsync($"device:{device}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 装置の設定と中身を比べ、不足と回収を判断する。設定がまだない装置は、不足も回収もないものとする（不整合の回収だけを返す）。
    /// </summary>
    /// <remarks>
    /// 取り込み中の元のスロットは、届ける数の上限では空きとして数える（足りなければ入れ先に使うため）。
    /// </remarks>
    public DeviceNeeds EvaluateDevice(DeviceId device) => Locked(() =>
    {
        var layout = Layout(device);
        var capacity = layout.SlotsPerStocker.Sum();
        var inStocker = _consumables.Values.Where(c => c.Location is InStocker s && s.Device == device).ToArray();
        var atTransferUnit = _consumables.Values.Where(c => c.Location is AtTransferUnit u && u.Device == device).ToArray();
        var lostAtSlot = _consumables.Values.Where(c => c.Location is UnknownLocation { Slot: not null } u && u.Device == device).ToArray();
        var reserved = _deliveries.Where(pair => pair.Value == device).Select(pair => _consumables[pair.Key]).ToArray();

        var free = Math.Max(0, capacity - inStocker.Length - atTransferUnit.Length - lostAtSlot.Length - reserved.Length);
        var mismatches = lostAtSlot
            .Where(c => !_recoveries.Contains(c.Id))
            .OrderBy(c => c.Id.Value, StringComparer.Ordinal)
            .Select(c => new DeviceRecovery(c.Id, c.Variety, KnownProcessType(c), RecoveryReason.Mismatch))
            .ToArray();
        if (!_settings.TryGetValue(device, out var counts))
        {
            return new DeviceNeeds(new Dictionary<VarietyId, int>(), mismatches, free);
        }

        // 届ける途中：届ける予約をしたものと、受け渡し部にある新品
        var inbound = reserved
            .Concat(atTransferUnit.Where(c => c.Condition == ConsumableCondition.New))
            .Where(c => c.Variety is not null)
            .GroupBy(c => c.Variety.GetValueOrDefault())
            .ToDictionary(g => g.Key, g => g.Count());
        var evaluation = StockerEvaluator.Evaluate(
            new StockerSetting(counts, capacity),
            new StockerContents(
                inStocker
                    .Where(c => c.Variety is not null)
                    .Select(c => new StockedItem(c.Id, c.Variety.GetValueOrDefault(), c.Condition))
                    .ToArray(),
                inbound,
                _recoveries));

        var discards = inStocker
            .Where(c => _discards.ContainsKey(c.Id) && !_recoveries.Contains(c.Id))
            .OrderBy(c => c.Id.Value, StringComparer.Ordinal)
            .Select(c => new DeviceRecovery(c.Id, c.Variety, KnownProcessType(c), RecoveryReason.Discard));
        var recoveries = mismatches
            .Concat(evaluation.Recoveries.Select(r =>
            {
                var c = _consumables[r.Id];
                return new DeviceRecovery(c.Id, c.Variety, KnownProcessType(c), r.Reason);
            }))
            .Concat(discards)
            .ToArray();

        return new DeviceNeeds(evaluation.Shortages, recoveries, free);
    });

    // ---- ステーションの在庫と予約 ----

    /// <summary>
    /// ステーションにある、届けられるその品種の新品（予約していないもの）。入庫した順に返す。
    /// </summary>
    /// <remarks>指名された消耗品は、指名した装置の候補（<see cref="GetDesignated"/>）なので、ここには入れない。</remarks>
    public IReadOnlyList<StationStock> GetStationStock(VarietyId variety) => Locked(() =>
    {
        ForgetInvalidDesignations();
        return _consumables.Values
            .Where(c => c.Variety == variety && IsDeliverable(c) && !_deliveries.ContainsKey(c.Id) && !_designations.ContainsKey(c.Id))
            .OrderBy(c => _arrivalOrder.GetValueOrDefault(c.Id))
            .Select(c => new StationStock(c.Id, ((AtStation)c.Location).Station))
            .ToArray();
    });

    /// <summary>
    /// 消耗品を、装置へ届けるものとして予約する。届ける途中の数に入り、ステーションの在庫から外れる。
    /// </summary>
    public void ReserveForDelivery(DeviceId device, IReadOnlyList<ConsumableId> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        Locked(() =>
        {
            Layout(device);
            foreach (var id in ids)
            {
                Consumable(id);
                _deliveries[id] = device;
            }
        });
    }

    /// <summary>
    /// 消耗品を、回収するものとして予約する。もう一度は回収を頼まず、不足の数にも空きの数にも入れない。
    /// </summary>
    public void ReserveForRecovery(IReadOnlyList<ConsumableId> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        Locked(() =>
        {
            foreach (var id in ids)
            {
                Consumable(id);
                _recoveries.Add(id);
            }
        });
    }

    /// <summary>
    /// 届ける・回収する予約を解く（搬送の要求を取りやめたとき）。
    /// </summary>
    public void ReleaseReservations(IReadOnlyList<ConsumableId> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        Locked(() =>
        {
            foreach (var id in ids)
            {
                _deliveries.Remove(id);
                _recoveries.Remove(id);
            }
        });
    }

    /// <summary>
    /// 上位システムに補充を頼む。頼んでまだ入庫されていない数を引いたぶんだけ頼む（1以上のときだけ）。
    /// </summary>
    /// <param name="quantity">今足りない数。</param>
    public Task RequestReplenishmentAsync(VarietyId variety, int quantity, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        return LockedAsync(
            () =>
            {
                var more = quantity - _outstanding.GetValueOrDefault(variety);
                if (more <= 0)
                {
                    return Task.CompletedTask;
                }

                _outstanding[variety] = quantity;
                return upstream.RequestReplenishmentAsync(variety, more, cancellationToken);
            },
            cancellationToken);
    }

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
                var location = LocationAfter(pending, transport, id);
                _consumables[id] = _consumables[id] with { Location = location };
                NoteArrival(id, location);
                ForgetReservations(pending, id);
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
        await PublishStockChangedAsync($"transport:{transport}", cancellationToken).ConfigureAwait(false);
        return report;
    }

    // ---- 内部 ----

    /// <summary>
    /// 装置で降ろしたら届ける予約を、装置から積み込んだら回収の予約と廃棄リクエストを外す（もう所在で数えられるため）。
    /// </summary>
    private void ForgetReservations(PendingTransfer pending, ConsumableId id)
    {
        if (pending.Device is null)
        {
            return;
        }

        if (pending.Side == TransferSide.Unload)
        {
            _deliveries.Remove(id);
        }
        else
        {
            _recoveries.Remove(id);
            _discards.Remove(id);
        }
    }

    /// <summary>
    /// ステーションに入ったら、入庫した順の番号を付ける。
    /// </summary>
    private void NoteArrival(ConsumableId id, ConsumableLocation location)
    {
        if (location is AtStation)
        {
            _arrivalOrder[id] = _nextArrival++;
        }
    }

    private Task PublishStockChangedAsync(string correlationId, CancellationToken cancellationToken) =>
        publisher.PublishAsync(new StockChanged(EventMetadata.Start($"stock:{correlationId}", timeProvider)), cancellationToken);

    /// <summary>装置にある消耗品の加工の種類（装置にあるものは、必ずわかっている）。</summary>
    private static ProcessTypeId KnownProcessType(ConsumableView consumable) =>
        consumable.ProcessType ?? throw new InvalidOperationException($"消耗品 {consumable.Id} の加工の種類がわかりません。");

    private ConsumableView Consumable(ConsumableId id) =>
        _consumables.TryGetValue(id, out var consumable)
            ? consumable
            : throw new InvalidOperationException($"消耗品 {id} は登録されていません。");

    private DeviceLayout Layout(DeviceId device) =>
        _devices.TryGetValue(device, out var layout)
            ? layout
            : throw new InvalidOperationException($"装置 {device} は登録されていません。");

    private ProcessTypeSettings ProcessType(ProcessTypeId id) =>
        _processTypes.TryGetValue(id, out var settings)
            ? settings
            : throw new InvalidOperationException($"加工の種類 {id} は登録されていません。");

    private ProcessTypeId VarietyProcessType(VarietyId variety) =>
        _varieties.TryGetValue(variety, out var processType)
            ? processType
            : throw new InvalidOperationException($"品種 {variety} は登録されていません。");

    private async Task LockedAsync(Action action, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            action();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<T> LockedAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return action();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task LockedAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await action().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static ConsumableLocation LocationAfter(PendingTransfer pending, TransportId transport, ConsumableId id) =>
        pending.Side == TransferSide.Load
            ? new OnAgv(transport)
            : pending.Device is { } device
                ? new AtTransferUnit(device, pending.Slots.TryGetValue(id, out var slot) ? slot : null)
                : new AtStation(pending.Station!.Value);

    /// <summary>
    /// 入れ先のスロットを、空いているうち番号が一番小さいものから決める。消耗品があるスロットと、入れ先に決めたスロットは使わない。
    /// 空いているスロットが足りなければ、取り込み中の元のスロットも番号の順に使う。
    /// </summary>
    private CargoSlot[] AllocateSlots(DeviceId device, ConsumableId[] cargo)
    {
        var layout = Layout(device);

        var taken = _consumables.Values
            .Select(c => c.Location switch
            {
                InStocker stocker when stocker.Device == device => stocker.Slot,
                AtTransferUnit unit when unit.Device == device => unit.PlannedSlot,
                UnknownLocation lost when lost.Device == device => lost.Slot,
                _ => null,
            })
            .OfType<SlotRef>()
            .Concat(_pending.Values.Where(p => p.Device == device && p.Side == TransferSide.Unload).SelectMany(p => p.Slots.Values))
            .ToHashSet();
        var homes = _consumables.Values
            .Select(c => c.Location is InProcessing processing && processing.Device == device ? processing.HomeSlot : null)
            .OfType<SlotRef>()
            .ToHashSet();

        var available = layout.AllSlots
            .Where(slot => !taken.Contains(slot))
            .OrderBy(slot => homes.Contains(slot))
            .ThenBy(slot => slot)
            .ToArray();
        if (available.Length < cargo.Length)
        {
            throw new InvalidOperationException($"装置 {device} のストッカーに、空いているスロットが足りません。");
        }

        return cargo.Select((id, index) => new CargoSlot(id, available[index])).ToArray();
    }

    /// <summary>
    /// 回収で取り出すスロット。ストッカーにあるものと、照合のエラーなどで現物がスロットにあるとわかっている所在不明のもの。
    /// </summary>
    private SlotRef SlotOf(ConsumableId id) =>
        _consumables.TryGetValue(id, out var consumable)
            ? consumable.Location switch
            {
                InStocker stocker => stocker.Slot,
                UnknownLocation { Slot: { } slot } => slot,
                _ => throw new InvalidOperationException($"消耗品 {id} はストッカーにありません。"),
            }
            : throw new InvalidOperationException($"消耗品 {id} は登録されていません。");

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
