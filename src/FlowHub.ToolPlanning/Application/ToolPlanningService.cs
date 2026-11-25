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

    /// <summary>積み込んだあとに取りやめて、ものを AGV に積んだまま止まっている搬送（AGV は配車が知っている）。</summary>
    private readonly HashSet<TransportId> _held = [];

    /// <summary>出した、AGV 自身を搬送元にした要求（届けるか返すか）のうち、まだ完了していないもの。</summary>
    private readonly Dictionary<TransportId, CarriedTrip> _carriedTrips = [];

    public void Dispose() => _gate.Dispose();

    /// <summary>
    /// 不足と回収の判断をやり直し、搬送の要求を出し直す。システムの運転状態が停止の間は、何もしない（スタートしたら判断する）。
    /// </summary>
    public async Task ReplanAsync(CancellationToken cancellationToken)
    {
        if (!dispatching.IsRunning)
        {
            return;
        }

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
    /// <returns>頼んだか。割り当て済みの要求に入っているか、ステーションにない消耗品なら、頼まずに断る。</returns>
    public async Task<ShipmentResult> RequestShipmentAsync(ConsumableId id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await CancelRequestContainingAsync(id, cancellationToken).ConfigureAwait(false))
            {
                return ShipmentResult.InAssignedTransport;
            }

            var result = await consumables.RequestShipmentAsync(id, cancellationToken).ConfigureAwait(false);
            await ReplanCoreAsync(cancellationToken).ConfigureAwait(false);
            return result;
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
        await CancelUnneededAsync(cancellationToken).ConfigureAwait(false);

        var now = timeProvider.GetUtcNow();

        // AGV が積んだまま止まっているものを、ステーションの在庫より先に使う
        await PlanCarriedAsync(now, cancellationToken).ConfigureAwait(false);

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
    /// 配車から、割り当てたあとの搬送を取りやめたと知らされた。消耗品の搬送でなければ扱わない。
    /// 予約を解き、ものを積んだまま止まったなら、その AGV を覚えて、判断をやり直す（停止中はやり直さない）。
    /// </summary>
    internal async Task OnTransportCanceledAsync(TransportCanceled canceled, CancellationToken cancellationToken)
    {
        if (canceled.Requester != ConsumablesService.Requester)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_requests.Remove(canceled.Transport, out var trip))
            {
                consumables.ReleaseReservations(trip.Cargo);
            }

            var loadedBy = canceled.Transport;
            if (_carriedTrips.Remove(canceled.Transport, out var carried))
            {
                consumables.ReleaseReservations(carried.Cargo);
                loadedBy = carried.LoadedBy; // AGV 自身を搬送元にした要求なら、ものは最初に積んだ搬送のまま
            }

            if (canceled.Loaded)
            {
                _held.Add(loadedBy);
            }

            if (dispatching.IsRunning)
            {
                await ReplanCoreAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
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
            if (_carriedTrips.Remove(completed.Transport, out var carried) && !carried.CargoRemains)
            {
                _held.Remove(carried.LoadedBy); // 全部を届けたか、返した
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 割り当て前の要求を取りやめ、予約を解く。割り当てたあとのものは、そのまま運ぶ（まるごと要らなくなったものは、別に取りやめる）。
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

        foreach (var (id, carried) in _carriedTrips.OrderBy(pair => pair.Key.Value).ToArray())
        {
            if (await dispatching.CancelTransportAsync(id, cancellationToken).ConfigureAwait(false))
            {
                consumables.ReleaseReservations(carried.Cargo);
                _carriedTrips.Remove(id);
            }
        }
    }

    /// <summary>
    /// 割り当て済みの要求が、まるごと要らなくなっていたら取りやめる（取りやめの知らせを受けて、予約を解いて判断をやり直す）。
    /// 届ける要求は、運んでいるどの品種も、その装置で不足していないとき（その要求のぶんは数えずに判断する）。
    /// 回収する要求は、運んでいるどの消耗品も回収が要らなくなったとき。積み込んだあとの回収は、ステーションへ運んでいる途中なので取りやめない。
    /// </summary>
    private async Task CancelUnneededAsync(CancellationToken cancellationToken)
    {
        foreach (var (id, trip) in _requests.OrderBy(pair => pair.Key.Value).ToArray())
        {
            if (trip.Kind == TripKind.Recovery && trip.Cargo.Any(c => consumables.GetConsumable(c).Location is OnAgv))
            {
                continue;
            }

            if (!IsStillNeeded(trip.Kind, trip.Device, trip.Cargo))
            {
                await dispatching.CancelAsync(id, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var (id, carried) in _carriedTrips.OrderBy(pair => pair.Key.Value).ToArray())
        {
            if (carried.Device is { } device && !IsStillNeeded(TripKind.Delivery, device, carried.Cargo))
            {
                await dispatching.CancelAsync(id, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>その要求のぶんの予約を外して判断し、要るものが1つでもあるか。予約は元に戻す。</summary>
    private bool IsStillNeeded(TripKind kind, DeviceId device, IReadOnlyList<ConsumableId> cargo)
    {
        consumables.ReleaseReservations(cargo);
        var needs = consumables.EvaluateDevice(device);
        if (kind == TripKind.Delivery)
        {
            consumables.ReserveForDelivery(device, cargo);
            return cargo.Any(c => consumables.GetConsumable(c).Variety is { } variety && needs.Shortages.ContainsKey(variety));
        }

        consumables.ReserveForRecovery(cargo);
        return cargo.Any(c => needs.Recoveries.Any(r => r.Consumable == c));
    }

    /// <summary>
    /// 積んだまま止まっている AGV のものの行き先を決める。届けられるものが装置で必要なら、不足の必要時刻が一番早い装置にだけ、
    /// 必要なものを届ける（残りがあれば、届けたあとに返す）。必要な装置がなければ、すぐにステーションへ返す。
    /// </summary>
    private async Task PlanCarriedAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var loadedBy in _held.OrderBy(id => id.Value).ToArray())
        {
            if (_carriedTrips.Values.Any(trip => trip.LoadedBy == loadedBy))
            {
                continue; // 割り当て済みの要求がある
            }

            var carried = consumables.GetCarried(loadedBy).Where(c => c.ReservedFor is null).ToArray();
            if (carried.Length == 0)
            {
                _held.Remove(loadedBy);
                continue;
            }

            var shortages = new List<Shortage>();
            var freeSlots = new Dictionary<DeviceId, int>();
            foreach (var device in consumables.Devices)
            {
                var needs = consumables.EvaluateDevice(device);
                freeSlots[device] = needs.FreeSlots;
                shortages.AddRange(ShortagesOf(device, needs, now));
            }

            var deliverable = carried.Where(c => c.Deliverable).ToArray();
            var target = shortages
                .Where(s => freeSlots[s.Device] > 0 && deliverable.Any(c => c.Variety == s.Variety))
                .OrderBy(s => s.Since)
                .ThenBy(s => s.Variety.Value, StringComparer.Ordinal)
                .ThenBy(s => s.Device.Value, StringComparer.Ordinal)
                .FirstOrDefault();

            if (target is null)
            {
                var station = consumables.StationEndpoint(SingleStation());
                await RequestCarriedAsync(new CarriedTrip(loadedBy, carried.Select(c => c.Id).ToArray(), Device: null, CargoRemains: false), station, now, cancellationToken).ConfigureAwait(false);
                continue;
            }

            // その装置で不足している品種を、空きの数まで選ぶ
            var picks = new List<ConsumableId>();
            var since = target.Since;
            foreach (var shortage in shortages.Where(s => s.Device == target.Device).OrderBy(s => s.Since).ThenBy(s => s.Variety.Value, StringComparer.Ordinal))
            {
                var take = deliverable
                    .Where(c => c.Variety == shortage.Variety && !picks.Contains(c.Id))
                    .Take(Math.Min(shortage.Count, freeSlots[target.Device] - picks.Count))
                    .Select(c => c.Id)
                    .ToArray();
                if (take.Length > 0)
                {
                    picks.AddRange(take);
                    since = since < shortage.Since ? since : shortage.Since;
                }
            }

            consumables.ReserveForDelivery(target.Device, picks);
            var trip = new CarriedTrip(loadedBy, picks, target.Device, CargoRemains: picks.Count < carried.Length);
            await RequestCarriedAsync(trip, consumables.DeviceEndpoint(target.Device), since, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RequestCarriedAsync(CarriedTrip trip, TransferEndpoint destination, DateTimeOffset neededBy, CancellationToken cancellationToken)
    {
        var id = await dispatching.RequestTransportFromAgvAsync(
            new AgvTransportOrder(ConsumablesService.Requester, trip.Cargo.Select(c => c.Value).ToArray(), trip.LoadedBy, destination.Panel, destination.Endpoint, neededBy)
            {
                CargoRemains = trip.CargoRemains,
            },
            cancellationToken).ConfigureAwait(false);
        _carriedTrips[id] = trip;
    }

    /// <summary>
    /// AGV 自身を搬送元にした要求。届けるなら届け先の装置、返すなら null。
    /// </summary>
    /// <param name="LoadedBy">そのものを積んだ、取りやめた搬送（AGV を指す）。</param>
    /// <param name="CargoRemains">降ろしたあとも、まだ積んだままか（一部だけ届けるとき）。</param>
    private sealed record CarriedTrip(TransportId LoadedBy, IReadOnlyList<ConsumableId> Cargo, DeviceId? Device, bool CargoRemains);

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
