using FlowHub.Consumables;
using FlowHub.Consumables.Application;
using FlowHub.Dispatching;
using FlowHub.ToolPlanning.Domain;

namespace FlowHub.ToolPlanning.Application;

/// <summary>
/// 消耗品の搬送計画の窓口。在庫が変わるたびに不足と回収の判断をやり直し、搬送の要求を配車に出す。
/// </summary>
/// <remarks>
/// やり直すときは、割り当て前の要求をいったん取りやめ、予約を解いてから判断し直して、要求を出し直す。
/// 不足を見つけた時刻と回収が必要だと判断した時刻は、続いているあいだ保つので、出し直しても必要時刻は変わらない。
/// </remarks>
public sealed class ToolPlanningService(
    ConsumablesService consumables,
    IDispatchingService dispatching,
    TimeProvider timeProvider) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<(DeviceId Device, VarietyId Variety), DateTimeOffset> _shortageSince = [];
    private readonly Dictionary<ConsumableId, DateTimeOffset> _recoverySince = [];

    /// <summary>出した要求のうち、まだ完了していないもの。</summary>
    private readonly Dictionary<TransportId, Trip> _requests = [];

    public void Dispose() => _gate.Dispose();

    /// <summary>
    /// 不足と回収の判断をやり直し、搬送の要求を出し直す。
    /// </summary>
    public async Task ReplanAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ReplanCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 上位システムから、ステーションの消耗品の出庫を指示された。
    /// 届ける要求に入っていれば、割り当て前ならその要求を取りやめて予約を解き、出庫を頼んでから、残りを出し直す。
    /// 割り当て済みなら受け付けない。
    /// </summary>
    /// <returns>受け付けたら true。割り当て済みの要求に入っているか、ステーションにない消耗品なら false。</returns>
    public async Task<bool> RequestShipmentAsync(ConsumableId id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await CancelRequestContainingAsync(id, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            var accepted = await consumables.RequestShipmentAsync(id, cancellationToken).ConfigureAwait(false);
            await ReplanCoreAsync(cancellationToken).ConfigureAwait(false);
            return accepted;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// ユーザーが、装置へ届ける消耗品を指名した。指名を記録して、判断をやり直す。
    /// </summary>
    public async Task DesignateAsync(DeviceId device, ConsumableId id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            consumables.Designate(device, id);
            await ReplanCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// ユーザーが、指名を取り消した。届ける要求に入っていれば、割り当て前ならその要求を取りやめてから取り消し、判断をやり直す。
    /// </summary>
    /// <returns>取り消したら true。割り当て済みの要求に入っていれば取り消さずに false。</returns>
    public async Task<bool> CancelDesignationAsync(ConsumableId id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await CancelRequestContainingAsync(id, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            consumables.CancelDesignation(id);
            await ReplanCoreAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 消耗品が入っている要求を、割り当て前なら取りやめて予約を解く。
    /// </summary>
    /// <returns>入っている要求がないか、取りやめたら true。割り当て済みなら false。</returns>
    private async Task<bool> CancelRequestContainingAsync(ConsumableId id, CancellationToken cancellationToken)
    {
        if (_requests.FirstOrDefault(pair => pair.Value.Cargo.Contains(id)) is not { Value: not null } request)
        {
            return true;
        }

        if (!await dispatching.CancelTransportAsync(request.Key, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        consumables.ReleaseReservations(request.Value.Cargo);
        _requests.Remove(request.Key);
        return true;
    }

    private async Task ReplanCoreAsync(CancellationToken cancellationToken)
    {
        await CancelWaitingAsync(cancellationToken).ConfigureAwait(false);

        var now = timeProvider.GetUtcNow();
        var shortages = new List<Shortage>();
        var recoveries = new List<Recovery>();
        var freeSlots = new Dictionary<DeviceId, int>();
        foreach (var device in consumables.Devices)
        {
            var needs = consumables.EvaluateDevice(device);
            freeSlots[device] = needs.FreeSlots;
            shortages.AddRange(ShortagesOf(device, needs, now));
            recoveries.AddRange(needs.Recoveries.Select(item => new Recovery(
                device,
                item,
                Since(_recoverySince, item.Consumable, now),
                consumables.GetProcessType(item.ProcessType))));
        }

        Forget(_recoverySince, recoveries.Select(r => r.Item.Consumable));

        var deliveries = TripPlanner.PlanDeliveries(
            shortages,
            freeSlots,
            variety => consumables.GetStationStock(variety).Select(s => s.Consumable).ToArray(),
            consumables.GetDesignated);

        // 必要時刻の順。同じなら届けるほうを先に頼む
        var trips = deliveries.Trips
            .Concat(TripPlanner.PlanRecoveries(recoveries))
            .OrderBy(t => t.NeededBy)
            .ThenBy(t => t.Kind)
            .ToArray();
        await RequestAsync(trips, cancellationToken).ConfigureAwait(false);

        foreach (var (variety, quantity) in deliveries.Lacking)
        {
            await consumables.RequestReplenishmentAsync(variety, quantity, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 配車から、搬送が完了したと知らされた。消耗品の搬送でなければ扱わない。
    /// </summary>
    internal async Task OnTransportCompletedAsync(TransportCompleted completed, CancellationToken cancellationToken)
    {
        if (completed.Requester != ConsumablesService.Requester)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _requests.Remove(completed.Transport);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 割り当て前の要求を取りやめ、予約を解く。割り当てたあとのものは、そのまま運ぶ。
    /// </summary>
    private async Task CancelWaitingAsync(CancellationToken cancellationToken)
    {
        foreach (var (id, trip) in _requests.OrderBy(pair => pair.Key.Value).ToArray())
        {
            if (await dispatching.CancelTransportAsync(id, cancellationToken).ConfigureAwait(false))
            {
                consumables.ReleaseReservations(trip.Cargo);
                _requests.Remove(id);
            }
        }
    }

    private Shortage[] ShortagesOf(DeviceId device, DeviceNeeds needs, DateTimeOffset now)
    {
        Forget(_shortageSince, needs.Shortages.Keys.Select(variety => (Device: device, Variety: variety)), key => key.Device == device);

        return needs.Shortages
            .Select(pair => new Shortage(
                device,
                pair.Key,
                pair.Value,
                Since(_shortageSince, (device, pair.Key), now),
                consumables.ProcessTypeOf(pair.Key)))
            .ToArray();
    }

    /// <summary>
    /// 要求を出す。消耗品を予約してから、配車に頼む。
    /// </summary>
    private async Task RequestAsync(IEnumerable<Trip> trips, CancellationToken cancellationToken)
    {
        var tripList = trips.ToArray();
        if (tripList.Length == 0)
        {
            return;
        }

        var station = SingleStation();
        foreach (var trip in tripList)
        {
            var deviceEnd = consumables.DeviceEndpoint(trip.Device);
            var stationEnd = consumables.StationEndpoint(station);
            var (source, destination) = trip.Kind == TripKind.Delivery ? (stationEnd, deviceEnd) : (deviceEnd, stationEnd);

            if (trip.Kind == TripKind.Delivery)
            {
                consumables.ReserveForDelivery(trip.Device, trip.Cargo);
            }
            else
            {
                consumables.ReserveForRecovery(trip.Cargo);
            }

            var id = await dispatching.RequestTransportAsync(
                new TransportOrder(
                    ConsumablesService.Requester,
                    trip.Cargo.Select(c => c.Value).ToArray(),
                    source.Panel,
                    source.Endpoint,
                    destination.Panel,
                    destination.Endpoint,
                    trip.ProcessType.Carrier,
                    trip.NeededBy),
                cancellationToken).ConfigureAwait(false);
            _requests[id] = trip;
        }
    }

    /// <summary>
    /// 使うステーション。ステーションの選び方のルールが決まるまでは、登録されている唯一のステーションを使う。
    /// </summary>
    private StationId SingleStation()
    {
        var stations = consumables.Stations;
        return stations.Count == 1
            ? stations[0]
            : throw new InvalidOperationException($"ステーションが {stations.Count} 台あります。ステーションの選び方のルールが決まるまでは、1台だけにしてください。");
    }

    private static DateTimeOffset Since<TKey>(Dictionary<TKey, DateTimeOffset> since, TKey key, DateTimeOffset now)
        where TKey : notnull
    {
        since.TryAdd(key, now);
        return since[key];
    }

    /// <summary>
    /// 続いていないもの（今回の判断に出てこなかったもの）の時刻を忘れる。
    /// </summary>
    private static void Forget<TKey>(Dictionary<TKey, DateTimeOffset> since, IEnumerable<TKey> current, Func<TKey, bool>? scope = null)
        where TKey : notnull
    {
        var keep = current.ToHashSet();
        foreach (var key in since.Keys.Where(key => (scope?.Invoke(key) ?? true) && !keep.Contains(key)).ToArray())
        {
            since.Remove(key);
        }
    }
}
