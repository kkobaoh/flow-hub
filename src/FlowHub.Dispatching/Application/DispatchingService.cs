using FlowHub.BuildingBlocks;
using FlowHub.Dispatching.Domain;
using FlowHub.Map;
using FlowHub.Routing;
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

    /// <summary>積み込んだあとに取りやめた搬送と、そのものを積んだまま止まっている AGV。</summary>
    private readonly Dictionary<TransportId, AgvId> _cargoHolders = [];

    /// <summary>システムの運転状態がスタートか。組み立ては、起動したらまず停止にする。</summary>
    private volatile bool _running = true;

    /// <inheritdoc />
    public bool IsRunning => _running;

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
    /// AGV 自身を搬送元にした搬送を頼む（ものを積んだまま止まっている AGV に、届けるか返すかさせる）。
    /// 待ち行列に入れ、その AGV が割り当てられる状態なら割り当てる。積み込みを飛ばして、搬送先へ向かう。
    /// </summary>
    /// <exception cref="InvalidOperationException">そのものを積んだまま止まっている AGV がいないとき。その AGV を搬送元にした搬送が、すでにあるとき。</exception>
    public Task<TransportId> RequestTransportFromAgvAsync(AgvTransportOrder order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        return RunAsync(async () =>
        {
            if (!_cargoHolders.TryGetValue(order.LoadedBy, out var holder) || !Agv(holder).Loaded)
            {
                throw new InvalidOperationException($"搬送 {order.LoadedBy} で積んだものを、積んだまま止まっている AGV はいません。");
            }

            if (_transports.Values.Any(t => t.Request.FromAgv == holder && t.Stage is not (TransportStage.Completed or TransportStage.Canceled or TransportStage.Interrupted)))
            {
                throw new InvalidOperationException($"{holder} を搬送元にした搬送が、すでにあります。");
            }

            var here = traffic.GetAgv(holder).StopsAt is { } stopsAt ? new PanelId(stopsAt.NodeId) : order.DestinationPanel;
            var transport = _queue.Add(new TransportRequest(
                order.Requester,
                order.CargoRefs,
                new TransferPoint(here, $"agv:{holder}"),
                new TransferPoint(order.DestinationPanel, order.DestinationEndpoint),
                Agv(holder).Carrier,
                order.NeededBy,
                FromAgv: holder,
                CargoRemains: order.CargoRemains,
                LoadedBy: order.LoadedBy));
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
    /// 搬送を取りやめる。割り当て前なら待ち行列から外す。割り当てたあとなら、AGV へのコマンドの単位で取りやめる
    /// （docs/domain.md「搬送の流れ」）。運転状態が停止でも受け付ける。
    /// </summary>
    /// <exception cref="InvalidOperationException">知らない搬送のとき。</exception>
    public Task<TransportCancelResult> CancelAsync(TransportId id, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var transport = _transports.TryGetValue(id, out var found)
                ? found
                : throw new InvalidOperationException($"搬送 {id} はありません。");

            if (transport.Stage == TransportStage.Waiting)
            {
                _queue.Cancel(id);
                return TransportCancelResult.Canceled;
            }

            var outcome = transport.RequestCancel();
            await ExecuteAsync(transport, outcome.Actions, cancellationToken).ConfigureAwait(false);
            return outcome.Result;
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

    /// <summary>
    /// AGV のコントロールステータスを切り替える（交通整理に頼む）。
    /// 搬送を割り当てている AGV は、マニュアル、ロック、切り離しには切り替えない。
    /// オートか割り当て停止に切り替えたら、待っていた充電に向かわせ、割り当てをやり直す。
    /// </summary>
    /// <param name="position">切り離しから切り替えるときに必須の位置。</param>
    /// <returns>切り替えを受け付けたか。位置のパネルをほかの AGV が予約していたら false。</returns>
    /// <exception cref="InvalidOperationException">搬送を割り当てている AGV を、マニュアル、ロック、切り離しに切り替えようとしたとき。交通整理が切り替えを受け付けないとき。</exception>
    public Task<bool> ChangeControlStatusAsync(AgvId id, ControlStatus target, Pose? position, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var agv = Agv(id);
            if (target is (ControlStatus.Manual or ControlStatus.Locked or ControlStatus.Detached) && agv.Active is not null)
            {
                throw new InvalidOperationException($"{id} は搬送を割り当てているため、{target} に切り替えられません。");
            }

            if (!await traffic.ChangeControlStatusAsync(id, target, position, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            if (target is ControlStatus.Manual or ControlStatus.Locked)
            {
                agv.Charging.OnRouteDiscarded(); // 交通整理が行き先を捨てたので、充電ポートへ向かっていても着かない
            }

            await AfterControlChangedAsync(id, cancellationToken).ConfigureAwait(false);
            return true;
        });

    /// <summary>コントロールステータスが変わった。システムで動かせるようになったなら、待っていた充電に向かわせ、割り当てをやり直す。</summary>
    private async Task AfterControlChangedAsync(AgvId id, CancellationToken cancellationToken)
    {
        if (!IsSystemControlled(id))
        {
            return;
        }

        if (Agv(id).Charging.State is ChargingState.SeekingPort or ChargingState.WaitingForPort)
        {
            await SeekChargingPortAsync(id, cancellationToken).ConfigureAwait(false);
        }

        await AssignWaitingAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>システムの判断で動かせる（運転状態がスタートで、AGV がオートか割り当て停止）か。</summary>
    private bool IsSystemControlled(AgvId id) => _running && traffic.GetAgv(id).Control is ControlStatus.Auto or ControlStatus.AssignmentPaused;

    // ---- 運転状態 ----

    /// <summary>
    /// 運転状態をスタートにする。一時停止していた搬送を続きから進め、待ち行列の搬送を割り当て、充電を待っていた AGV を充電ポートへ向かわせる。
    /// <see cref="OperationStarted"/> で知らせる（搬送計画が、不足と回収の判断を1回行う）。すでにスタートなら何もしない。
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            if (_running)
            {
                return false;
            }

            _running = true;
            traffic.SetEvacuationEnabled(true);
            foreach (var transport in _agvs.Values.Select(agv => agv.Active).OfType<Transport>().ToArray())
            {
                await ExecuteAsync(transport, ResumeActions(transport, PauseReason.OperationStopped), cancellationToken).ConfigureAwait(false);
            }

            await AssignWaitingAsync(cancellationToken).ConfigureAwait(false);
            foreach (var (id, agv) in _agvs.ToArray())
            {
                if (agv.Charging.State is ChargingState.SeekingPort or ChargingState.WaitingForPort)
                {
                    await SeekChargingPortAsync(id, cancellationToken).ConfigureAwait(false);
                }
            }

            await PublishAsync("operation", m => new OperationStarted(m), cancellationToken).ConfigureAwait(false);
            return true;
        });

    /// <summary>
    /// 運転状態を停止にする。新しい割り当て、充電に向かわせること、通り道を塞いでいる AGV をどかすことをやめ、
    /// 割り当てている搬送は一時停止する（次のコマンドを出さない）。通路にいる AGV は、待機パネルへ動かして止める。すでに停止なら何もしない。
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            if (!_running)
            {
                return false;
            }

            _running = false;
            traffic.SetEvacuationEnabled(false);
            foreach (var transport in _agvs.Values.Select(agv => agv.Active).OfType<Transport>())
            {
                transport.Pause(PauseReason.OperationStopped);
            }

            await ParkAsync(cancellationToken).ConfigureAwait(false);
            return true;
        });

    /// <summary>
    /// 一時停止の理由を外して、続きのコマンドを返す。理由がすべて外れて移動の段階なら、もう一度行き先へ向かわせる
    /// （停止中に待機パネルへ動かしたり、その場に止めたりして、行き先を上書きしたかもしれないため）。
    /// </summary>
    private static IReadOnlyList<TransportAction> ResumeActions(Transport transport, PauseReason reason)
    {
        var actions = transport.Resume(reason);
        return actions.Count == 0 && !transport.IsPaused && transport.CurrentMove() is { } move ? [move] : actions;
    }

    /// <summary>
    /// 通路にいる、システムが動かす AGV を、待機パネルへ動かして止める（docs/domain.md「スタートから停止にしたとき」）。
    /// 充電ピンのある待機パネル、一番近い待機パネル、一番近い受け渡しパネルの順に選び、どれも空いていなければその場で止めて知らせる。
    /// 取り合うときは、充電残量が少ない AGV（わからなければ最後）、同じなら空いている候補に近い AGV から決める。
    /// </summary>
    private async Task ParkAsync(CancellationToken cancellationToken)
    {
        if (mapStore.Current is not { } map)
        {
            return; // マップがなければ、AGV もマップの上にいない
        }

        var views = _agvs.Keys.ToDictionary(id => id, traffic.GetAgv);

        // 空いていないパネル：ほかの AGV がいる・止まる、充電ポートへ向かっている先
        var taken = new HashSet<PanelId>(views.Values
            .SelectMany(view => new[] { view.Position, view.StopsAt })
            .OfType<Pose>()
            .Select(pose => new PanelId(pose.NodeId)));
        taken.UnionWith(_agvs.Values.Select(agv => agv.Charging.Port).OfType<PanelId>());

        // 特殊消耗品受け渡しパネルは通過できるので、通路と同じに扱う
        var corridor = map.Panels.Where(p => p.Type is PanelType.Normal or PanelType.SpecialTransfer).Select(p => p.Id).ToHashSet();

        // 通路で緊急停止中の AGV は、待機パネルへ向かわせず、アラームクリアのあとも続きを走らないように、その場を行き先にしておく
        foreach (var (id, agv) in _agvs.Where(pair => IsStoppedInCorridor(pair.Value, views[pair.Key], corridor)).ToArray())
        {
            await traffic.MoveAgvAsync(id, new PanelId(views[id].StopsAt!.NodeId), neededBy: null, $"park:{id}", cancellationToken).ConfigureAwait(false);
        }

        var parking = _agvs
            .Where(pair => ShouldPark(pair.Value, views[pair.Key], corridor))
            .Select(pair => pair.Key)
            .ToArray();

        var pins = map.Panels.Where(p => p.Type == PanelType.Standby && p.HasChargingPin).Select(p => p.Id).ToArray();
        var standbys = map.Panels.Where(p => p.Type == PanelType.Standby && !p.HasChargingPin).Select(p => p.Id).ToArray();
        var transfers = map.Panels.Where(p => p.Type == PanelType.Transfer).Select(p => p.Id).ToArray();
        var allCandidates = pins.Concat(standbys).Concat(transfers).ToArray();

        bool IsFree(AgvId id, PanelId panel) => !taken.Contains(panel) && !traffic.IsReservedByOther(panel, id);
        (PanelId Panel, TimeSpan Cost)? Nearest(AgvId id, IEnumerable<PanelId> panels) => panels
            .Where(panel => IsFree(id, panel))
            .Select(panel => (Panel: panel, Cost: traffic.EstimateMoveCost(id, panel)))
            .Where(c => c.Cost is not null)
            .Select(c => (c.Panel, Cost: c.Cost!.Value))
            .OrderBy(c => c.Cost)
            .ThenBy(c => c.Panel.Value, StringComparer.Ordinal)
            .Cast<(PanelId, TimeSpan)?>()
            .FirstOrDefault();

        var ordered = parking
            .OrderBy(id => _agvs[id].Charging.BatteryPercent is null)
            .ThenBy(id => _agvs[id].Charging.BatteryPercent)
            .ThenBy(id => Nearest(id, allCandidates)?.Cost ?? TimeSpan.MaxValue)
            .ThenBy(id => id.Value, StringComparer.Ordinal)
            .ToArray();

        foreach (var id in ordered)
        {
            var correlationId = $"park:{id}";
            var chosen = Nearest(id, pins) ?? Nearest(id, standbys) ?? Nearest(id, transfers);
            if (chosen is { } spot)
            {
                taken.Add(spot.Panel);
                await traffic.MoveAgvAsync(id, spot.Panel, neededBy: null, correlationId, cancellationToken).ConfigureAwait(false);
                continue;
            }

            // どこも空いていない：止まれる最初の位置で止めて、知らせる
            var stopsAt = new PanelId(views[id].StopsAt!.NodeId);
            await traffic.MoveAgvAsync(id, stopsAt, neededBy: null, correlationId, cancellationToken).ConfigureAwait(false);
            await PublishAsync(correlationId, m => new NoParkingSpace(m, id), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 停止にしたときに、待機パネルへ動かす AGV か。システムが動かす（オートか割り当て停止）、止まれる最初の位置が通路、
    /// 走れる状態（待機中か走行中。緊急停止中は含めない）、充電ポートへ向かっていない・充電していない、のすべてを満たすもの。
    /// </summary>
    private static bool ShouldPark(AgvDispatch agv, AgvView view, HashSet<PanelId> corridor) =>
        IsSystemMovedInCorridor(view, corridor)
        && view.State is AgvState.Idle or AgvState.Moving
        && agv.Charging.State is not (ChargingState.GoingToPort or ChargingState.Charging);

    /// <summary>停止にしたときに、通路で緊急停止中の、システムが動かす AGV か（充電ポートへ向かっているものは除く）。</summary>
    private static bool IsStoppedInCorridor(AgvDispatch agv, AgvView view, HashSet<PanelId> corridor) =>
        IsSystemMovedInCorridor(view, corridor)
        && view.State == AgvState.EmergencyStopped
        && agv.Charging.State is not (ChargingState.GoingToPort or ChargingState.Charging);

    private static bool IsSystemMovedInCorridor(AgvView view, HashSet<PanelId> corridor) =>
        view.Control is ControlStatus.Auto or ControlStatus.AssignmentPaused
        && view.StopsAt is { } stopsAt
        && corridor.Contains(new PanelId(stopsAt.NodeId));

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
            Agv(stopped.Agv).Active?.Pause(PauseReason.AgvStopped);
            return Task.FromResult(true);
        });

    internal Task OnAgvResumedAsync(AgvResumed resumed, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            if (Agv(resumed.Agv).Active is { } transport)
            {
                await ExecuteAsync(transport, ResumeActions(transport, PauseReason.AgvStopped), cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// 充電ポートが空いた。行ける充電ポートがなくて待っている AGV に、もう一度探させる。
    /// 充電残量が少ない AGV から、同じなら空いた充電ポートに近い AGV から探させる。停止中は何もしない（スタートしたときに探させる）。
    /// </summary>
    internal Task OnChargingPortReleasedAsync(ChargingPortReleased released, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            // 停止中は、充電ポートを探す処理が向かわせない（スタートしたときに探させる）
            var waiting = _agvs
                .Where(pair => pair.Value.Charging.State == ChargingState.WaitingForPort)
                .Select(pair => pair.Key)
                .OrderBy(id => _agvs[id].Charging.BatteryPercent is null)
                .ThenBy(id => _agvs[id].Charging.BatteryPercent)
                .ThenBy(id => traffic.EstimateMoveCost(id, released.Panel) ?? TimeSpan.MaxValue)
                .ThenBy(id => id.Value, StringComparer.Ordinal)
                .ToArray();
            foreach (var id in waiting)
            {
                await SeekChargingPortAsync(id, cancellationToken).ConfigureAwait(false);
            }

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
                    var result = await traffic.MoveAgvAsync(move.Agv, move.To, request.NeededBy, correlationId, cancellationToken).ConfigureAwait(false);
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
                    Agv(transport.AssignedAgv!.Value).Loaded = request.CargoRemains; // 降ろした（一部だけなら、まだ積んだまま）
                    if (request.LoadedBy is { } loadedBy && !request.CargoRemains)
                    {
                        _cargoHolders.Remove(loadedBy);
                    }

                    await PublishAsync(correlationId, m => new TransportCompleted(m, completed.Transport, request.Requester), cancellationToken).ConfigureAwait(false);
                    await OnTransportFinishedAsync(transport.AssignedAgv!.Value, cancellationToken).ConfigureAwait(false);
                    break;

                case StopAgv stop:
                    await traffic.StopAgvAsync(stop.Agv, correlationId, cancellationToken).ConfigureAwait(false);
                    break;

                case NotifyCanceled canceled:
                    await PublishAsync(correlationId, m => new TransportCanceled(m, canceled.Transport, request.Requester, canceled.Agv, canceled.Loaded), cancellationToken).ConfigureAwait(false);
                    Agv(canceled.Agv).Loaded = canceled.Loaded;
                    if (canceled.Loaded && request.LoadedBy is null)
                    {
                        _cargoHolders[canceled.Transport] = canceled.Agv; // AGV 自身が搬送元の搬送なら、最初に積んだ搬送のまま
                    }

                    await OnTransportFinishedAsync(canceled.Agv, cancellationToken).ConfigureAwait(false);
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
    /// <remarks>マニュアル、ロック、切り離しの AGV は動かさず、探している状態のまま待つ（オートか割り当て停止に戻したら探す）。</remarks>
    private async Task SeekChargingPortAsync(AgvId id, CancellationToken cancellationToken)
    {
        if (!IsSystemControlled(id))
        {
            return;
        }

        var charging = Agv(id).Charging;
        var ports = CurrentMap().Panels.Where(p => p.HasChargingPin).Select(p => p.Id);
        var correlationId = $"charge:{id}";

        // ほかの AGV が予約している、または向かっている充電ポートは、使っているものとみなす
        bool IsTaken(PanelId port) =>
            traffic.IsReservedByOther(port, id)
            || _agvs.Any(other => other.Key != id && other.Value.Charging.State == ChargingState.GoingToPort && other.Value.Charging.Port == port);

        if (ChargingPortSelector.Choose(ports, IsTaken, port => traffic.EstimateMoveCost(id, port)) is not { } chosen)
        {
            charging.WaitForPort();
            await PublishAsync(correlationId, m => new NoChargingPortAvailable(m, id), cancellationToken).ConfigureAwait(false);
            return;
        }

        charging.GoTo(chosen);
        var result = await traffic.MoveAgvAsync(id, chosen, neededBy: null, correlationId, cancellationToken).ConfigureAwait(false); // 搬送のない移動
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
        if (!_running)
        {
            return; // 停止中は割り当てない（スタートしたら割り当てる）
        }

        var candidates = _agvs.Select(pair => CandidateOf(pair.Key, pair.Value)).OfType<AgvCandidate>().ToArray();
        var assignments = _queue.AssignWaiting(candidates, (candidate, source) => traffic.EstimateMoveCost(candidate.Id, source));

        foreach (var assignment in assignments)
        {
            Agv(assignment.Transport.AssignedAgv!.Value).Active = assignment.Transport;
            await ExecuteAsync(assignment.Transport, assignment.Actions, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 割り当ての候補。オートで、ものを積んだまま止まっていない AGV だけを、空いているものとする。位置がわからない AGV は候補にしない。充電残量の通知がまだなければ、足りているものとみなす。
    /// </summary>
    private AgvCandidate? CandidateOf(AgvId id, AgvDispatch agv)
    {
        var view = traffic.GetAgv(id);
        if (view.Position is null)
        {
            return null;
        }

        var isIdle = view.State == AgvState.Idle
            && view.Control == ControlStatus.Auto
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
            view.CumulativeDistanceMm,
            agv.Loaded);
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

        /// <summary>搬送を取りやめて、ものを積んだまま止まっている（届けるか返すまで、ほかの搬送は割り当てない）。</summary>
        public bool Loaded { get; set; }
    }
}
