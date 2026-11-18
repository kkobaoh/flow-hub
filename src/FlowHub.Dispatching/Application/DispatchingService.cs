using FlowHub.BuildingBlocks;
using FlowHub.Dispatching.Domain;
using FlowHub.Map;
using FlowHub.Traffic;

namespace FlowHub.Dispatching.Application;

/// <summary>
/// 配車の窓口。搬送の頼みを受けて待ち行列に入れ、AGV を割り当て、交通整理からのイベントで搬送と充電を進める。
/// </summary>
/// <remarks>
/// 交通整理のイベントを受ける窓口は、同じプロジェクトの購読者から呼ぶので internal にする。
/// 処理は1つずつ順に行う（同時に呼ばれても状態が崩れないようにする）。
/// </remarks>
public sealed class DispatchingService(
    ITrafficService traffic,
    IMapStore mapStore,
    IEventPublisher publisher,
    TimeProvider timeProvider) : IDispatchingService, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TransportQueue _queue = new();
    private readonly Dictionary<TransportId, Transport> _transports = [];
    private readonly Dictionary<AgvId, AgvDispatch> _agvs = [];

    public void Dispose() => _gate.Dispose();

    // ---- 登録と頼み ----

    /// <summary>
    /// AGV を登録する。キャリアの種類と充電の設定値は、AGV の種類ごとの設定から渡す。
    /// </summary>
    public void RegisterAgv(AgvId id, CarrierTypeId carrier, int goChargeBelowPercent, int resumeAtPercent)
    {
        var policy = new ChargingPolicy(goChargeBelowPercent, resumeAtPercent);
        _gate.Wait();
        try
        {
            if (!_agvs.TryAdd(id, new AgvDispatch(carrier, policy, new AgvCharging(id, policy))))
            {
                throw new InvalidOperationException($"{id} はすでに登録されています。");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 搬送を頼む。待ち行列に入れ、割り当てられる AGV がいれば割り当てる。
    /// </summary>
    public Task<TransportId> RequestTransportAsync(TransportOrder order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        return RunAsync(async () =>
        {
            var transport = _queue.Add(new TransportRequest(
                order.Requester,
                order.CargoRefs,
                new TransferPoint(order.SourcePanel, order.SourceEndpoint),
                new TransferPoint(order.DestinationPanel, order.DestinationEndpoint),
                order.Carrier,
                order.NeededBy));
            _transports.Add(transport.Id, transport);

            await AssignWaitingAsync(cancellationToken).ConfigureAwait(false);
            return transport.Id;
        });
    }

    /// <summary>
    /// 割り当て前の搬送を取りやめる。割り当てたあとなら取りやめずに false を返す（そのまま運ぶ）。
    /// </summary>
    public Task<bool> CancelTransportAsync(TransportId id, CancellationToken cancellationToken) =>
        RunAsync(() =>
        {
            if (!_transports.ContainsKey(id))
            {
                throw new InvalidOperationException($"搬送 {id} はありません。");
            }

            if (!_queue.Waiting.Any(t => t.Id == id))
            {
                return Task.FromResult(false);
            }

            _queue.Cancel(id);
            return Task.FromResult(true);
        });

    /// <summary>
    /// 受け渡しの相手が、受け渡しを終えた（消耗品の管理から）。
    /// </summary>
    public Task ReportTransferCompletedAsync(TransportId id, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var transport = _transports.TryGetValue(id, out var found)
                ? found
                : throw new InvalidOperationException($"搬送 {id} はありません。");

            await ExecuteAsync(transport, transport.OnTransferCompleted().Actions, cancellationToken).ConfigureAwait(false);
            return true;
        });

    // ---- 交通整理からのイベント（購読者から呼ぶ） ----

    internal Task OnAgvArrivedAsync(AgvArrived arrived, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var agv = Agv(arrived.Agv);
            if (agv.Active is { } transport)
            {
                await ExecuteAsync(transport, transport.OnArrived(arrived.Panel).Actions, cancellationToken).ConfigureAwait(false);
            }
            else if (agv.Charging.OnArrived(arrived.Panel) == ChargingDecision.Recovered)
            {
                await AssignWaitingAsync(cancellationToken).ConfigureAwait(false);
            }

            return true;
        });

    internal Task OnCarrierLoweredAsync(CarrierLowered lowered, CancellationToken cancellationToken) =>
        OnTransportEventAsync(lowered.Agv, transport => transport.OnCarrierLowered(), cancellationToken);

    internal Task OnCarrierRaisedAsync(CarrierRaised raised, CancellationToken cancellationToken) =>
        OnTransportEventAsync(raised.Agv, transport => transport.OnCarrierRaised(), cancellationToken);

    internal Task OnAgvStoppedAsync(AgvStopped stopped, CancellationToken cancellationToken) =>
        RunAsync(() =>
        {
            Agv(stopped.Agv).Active?.Pause();
            return Task.FromResult(true);
        });

    internal Task OnAgvResumedAsync(AgvResumed resumed, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            if (Agv(resumed.Agv).Active is { } transport)
            {
                await ExecuteAsync(transport, transport.Resume(), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await AssignWaitingAsync(cancellationToken).ConfigureAwait(false); // AGV が空いた
            }

            return true;
        });

    internal Task OnAgvFaultedAsync(AgvFaulted faulted, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var agv = Agv(faulted.Agv);
            agv.Charging.OnFaulted();
            if (agv.Active is { } transport)
            {
                await ExecuteAsync(transport, transport.Interrupt(), cancellationToken).ConfigureAwait(false);
            }

            return true;
        });

    internal Task OnAgvPositionResetCompletedAsync(AgvPositionResetCompleted reset, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            if (Agv(reset.Agv).Charging.State == ChargingState.SeekingPort)
            {
                await SeekChargingPortAsync(reset.Agv, cancellationToken).ConfigureAwait(false);
            }

            await AssignWaitingAsync(cancellationToken).ConfigureAwait(false); // AGV が空いた
            return true;
        });

    internal Task OnAgvBatteryChangedAsync(AgvBatteryChanged changed, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var agv = Agv(changed.Agv);
            var decision = agv.Charging.OnBatteryChanged(changed.Percent, SituationOf(changed.Agv));
            await OnChargingDecisionAsync(changed.Agv, decision, cancellationToken).ConfigureAwait(false);
            return true;
        });

    // ---- 内部 ----

    private Task<bool> OnTransportEventAsync(AgvId id, Func<Transport, TransportEventResult> handle, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            if (Agv(id).Active is { } transport)
            {
                await ExecuteAsync(transport, handle(transport).Actions, cancellationToken).ConfigureAwait(false);
            }

            return true;
        });

    /// <summary>
    /// 搬送の状態遷移が返した、頼むことと知らせることを行う。
    /// </summary>
    private async Task ExecuteAsync(Transport transport, IReadOnlyList<TransportAction> actions, CancellationToken cancellationToken)
    {
        var correlationId = $"transport:{transport.Id}";
        var request = transport.Request;
        foreach (var action in actions)
        {
            switch (action)
            {
                case MoveAgv move:
                    var result = await traffic.MoveAgvAsync(move.Agv, move.To, correlationId, cancellationToken).ConfigureAwait(false);
                    if (result == MoveResult.NoRoute)
                    {
                        await ExecuteAsync(transport, transport.Interrupt(), cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    if (result == MoveResult.Waiting)
                    {
                        await PublishAsync(correlationId, m => new MoveBlocked(m, move.Agv, move.To), cancellationToken).ConfigureAwait(false);
                    }

                    break;

                case LowerCarrier lower:
                    await traffic.LowerCarrierAsync(lower.Agv, correlationId, cancellationToken).ConfigureAwait(false);
                    break;

                case RaiseCarrier raise:
                    await traffic.RaiseCarrierAsync(raise.Agv, correlationId, cancellationToken).ConfigureAwait(false);
                    break;

                case RequestTransferPermission permission:
                    await PublishAsync(correlationId, m => new TransferPermissionRequested(m, permission.Transport, request.Requester, request.CargoRefs, permission.At.Endpoint, permission.Side), cancellationToken).ConfigureAwait(false);
                    break;

                case NotifyCompleted completed:
                    await PublishAsync(correlationId, m => new TransportCompleted(m, completed.Transport, request.Requester), cancellationToken).ConfigureAwait(false);
                    await OnTransportFinishedAsync(transport.AssignedAgv!.Value, cancellationToken).ConfigureAwait(false);
                    break;

                case NotifyInterrupted interrupted:
                    await PublishAsync(correlationId, m => new TransportInterrupted(m, interrupted.Transport, request.Requester), cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    throw new InvalidOperationException($"未知の頼みです：{action.GetType().Name}");
            }
        }
    }

    /// <summary>
    /// 搬送を終えた。充電を待っていたなら充電に向かわせ、割り当てをやり直す。
    /// </summary>
    private async Task OnTransportFinishedAsync(AgvId id, CancellationToken cancellationToken)
    {
        var agv = Agv(id);
        agv.Active = null;
        var decision = agv.Charging.OnTransportFinished(SituationOf(id));
        await OnChargingDecisionAsync(id, decision, cancellationToken).ConfigureAwait(false);
        await AssignWaitingAsync(cancellationToken).ConfigureAwait(false); // AGV が空いた
    }

    private async Task OnChargingDecisionAsync(AgvId id, ChargingDecision decision, CancellationToken cancellationToken)
    {
        switch (decision)
        {
            case ChargingDecision.FindPort:
                await SeekChargingPortAsync(id, cancellationToken).ConfigureAwait(false);
                break;
            case ChargingDecision.Recovered:
                await AssignWaitingAsync(cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// 一番近い、空いている充電ポートへ向かわせる。なければ待たせて知らせる。
    /// </summary>
    private async Task SeekChargingPortAsync(AgvId id, CancellationToken cancellationToken)
    {
        var charging = Agv(id).Charging;
        var ports = CurrentMap().Panels.Where(p => p.HasChargingPin).Select(p => p.Id);
        var correlationId = $"charge:{id}";

        if (ChargingPortSelector.Choose(ports, port => traffic.IsReservedByOther(port, id), port => traffic.EstimateMoveCost(id, port)) is not { } chosen)
        {
            charging.WaitForPort();
            await PublishAsync(correlationId, m => new NoChargingPortAvailable(m, id), cancellationToken).ConfigureAwait(false);
            return;
        }

        charging.GoTo(chosen);
        var result = await traffic.MoveAgvAsync(id, chosen, correlationId, cancellationToken).ConfigureAwait(false);
        if (result == MoveResult.Waiting)
        {
            await PublishAsync(correlationId, m => new MoveBlocked(m, id, chosen), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 割り当て待ちの搬送に、割り当てられる AGV を割り当てる。
    /// </summary>
    private async Task AssignWaitingAsync(CancellationToken cancellationToken)
    {
        var candidates = _agvs.Select(pair => CandidateOf(pair.Key, pair.Value)).OfType<AgvCandidate>().ToArray();
        var assignments = _queue.AssignWaiting(candidates, (candidate, source) => traffic.EstimateMoveCost(candidate.Id, source));

        foreach (var assignment in assignments)
        {
            Agv(assignment.Transport.AssignedAgv!.Value).Active = assignment.Transport;
            await ExecuteAsync(assignment.Transport, assignment.Actions, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 割り当ての候補。位置がわからない AGV は候補にしない。充電残量の通知がまだなければ、足りているものとみなす。
    /// </summary>
    private AgvCandidate? CandidateOf(AgvId id, AgvDispatch agv)
    {
        var view = traffic.GetAgv(id);
        if (view.Position is null)
        {
            return null;
        }

        var isIdle = view.State == AgvState.Idle
            && view.CarrierRaised
            && agv.Active is null
            && agv.Charging.State == ChargingState.Normal;

        return new AgvCandidate(
            id,
            view.Position,
            isIdle,
            agv.Charging.BatteryPercent ?? 100,
            agv.Charging.RecoveringCharge,
            agv.Policy,
            agv.Carrier,
            view.CumulativeDistanceMm);
    }

    /// <summary>
    /// 充電の判断に使う AGV の様子（充電ピンのあるパネルにいるか、搬送の途中か）。
    /// </summary>
    private AgvSituation SituationOf(AgvId id)
    {
        var position = traffic.GetAgv(id).Position;
        var onPin = position is not null
            && CurrentMap().Panels.Any(p => p.Id.Value == position.NodeId && p.HasChargingPin);
        return new AgvSituation(onPin, Agv(id).Active is not null);
    }

    private Task PublishAsync(string correlationId, Func<EventMetadata, IIntegrationEvent> create, CancellationToken cancellationToken) =>
        publisher.PublishAsync(create(EventMetadata.Start(correlationId, timeProvider)), cancellationToken);

    private async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private MapSnapshot CurrentMap() =>
        mapStore.Current ?? throw new InvalidOperationException("マップがまだ公開されていません。");

    private AgvDispatch Agv(AgvId id) =>
        _agvs.TryGetValue(id, out var agv) ? agv : throw new InvalidOperationException($"{id} は登録されていません。");

    /// <summary>
    /// 登録した AGV ごとの、配車の情報。
    /// </summary>
    private sealed class AgvDispatch(CarrierTypeId carrier, ChargingPolicy policy, AgvCharging charging)
    {
        public CarrierTypeId Carrier { get; } = carrier;

        public ChargingPolicy Policy { get; } = policy;

        public AgvCharging Charging { get; } = charging;

        /// <summary>割り当てている搬送（中断したものも含む）。なければ null。</summary>
        public Transport? Active { get; set; }
    }
}
