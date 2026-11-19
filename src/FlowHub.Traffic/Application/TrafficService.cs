using FlowHub.BuildingBlocks;
using FlowHub.Map;
using FlowHub.PathPlanning;
using FlowHub.Routing;
using FlowHub.Traffic.Domain;

namespace FlowHub.Traffic.Application;

/// <summary>
/// 交通整理の窓口。配車からの頼み（AGV を動かす、キャリアを上げ下げする、移動コストを求める）と、
/// AGV からのリクエストや通知を受け、起きたことをイベントで知らせる。
/// </summary>
/// <remarks>
/// AGV からのリクエストと通知を受ける窓口は、同じプロジェクトの AGV との通信の部分から呼ぶので internal にする。
/// 処理は1つずつ順に行う（同時に呼ばれても状態が崩れないようにする）。
/// </remarks>
/// <param name="commandNumbers">次に付けるコマンドの番号の保存場所。省略したら、メモリーの中にだけ持つ（再起動すると 1 に戻る）。</param>
/// <param name="planning">全台の経路の計画の設定。省略したら既定値。</param>
public sealed class TrafficService(
    IMapStore mapStore,
    IEventPublisher publisher,
    TimeProvider timeProvider,
    ICommandNumberStore? commandNumbers = null,
    TrafficPlanningOptions? planning = null) : ITrafficService, IDisposable
{
    private readonly ICommandNumberStore _commandNumbers = commandNumbers ?? new InMemoryCommandNumberStore();
    private readonly TrafficPlanningOptions _planning = planning ?? new TrafficPlanningOptions();
    private readonly PrioritizedPlanner _fleetPlanner = new();
    private PassingOrderGate? _passingGate;
    private long _requestOrder;

    /// <summary>塞がれて待っている AGV がいるかもしれない変化（着いた、再開した）があり、計画を立て直す必要がある。</summary>
    private bool _replanRequested;

    private readonly SemaphoreSlim _gate = new(1, 1);

    public void Dispose() => _gate.Dispose();
    private readonly PanelReservations _reservations = new();
    private readonly Dictionary<AgvId, AgvEntry> _agvs = [];
    private readonly RoutePlanner _planner = new();

    // ---- AGV の登録と参照 ----

    /// <summary>
    /// AGV を登録する。速さは移動コストの計算に使う（AGV の種類ごとの設定から渡す）。
    /// </summary>
    public void RegisterAgv(AgvId id, MotionProfile motion)
    {
        ArgumentNullException.ThrowIfNull(motion);
        _gate.Wait();
        try
        {
            _passingGate ??= new PassingOrderGate(agv => _agvs.TryGetValue(agv, out var entry) ? entry.Travel.RemainingRoutePanels : null);
            if (!_agvs.TryAdd(id, new AgvEntry(new AgvTravel(id, _reservations, _commandNumbers.NextNumberOf(id), _passingGate), motion)))
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
    /// AGV の今の様子。
    /// </summary>
    public AgvView GetAgv(AgvId id)
    {
        _gate.Wait();
        try
        {
            var travel = Entry(id).Travel;
            return new AgvView(id, travel.Position, ToState(travel.Status), travel.Carrier == CarrierState.Raised, travel.CumulativeDistanceMm);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// AGV の今の位置から、パネルまでの移動コスト（所要時間）。経路が見つからなければ null。予約は考えない。
    /// </summary>
    public TimeSpan? EstimateMoveCost(AgvId id, PanelId destination)
    {
        _gate.Wait();
        try
        {
            var entry = Entry(id);
            if (entry.Travel.Position is null)
            {
                return null;
            }

            var route = Plan(entry, destination);
            return route.Found ? route.Cost : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// ほかの AGV がそのパネルを予約しているか（充電ポートが使われているかの判断などに使う）。
    /// </summary>
    public bool IsReservedByOther(PanelId panel, AgvId self)
    {
        _gate.Wait();
        try
        {
            return _reservations.OwnerOf(panel) is { } owner && owner != self;
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---- 配車などからの頼み ----

    /// <summary>
    /// 位置を上書きする（最初の登録も同じ）。指定のパネルをほかの AGV が予約していたら false。
    /// </summary>
    public Task<bool> ResetPositionAsync(AgvId id, Pose position, CancellationToken cancellationToken) =>
        RunAsync(id, null, entry => entry.Travel.ResetPosition(position).Succeeded, cancellationToken);

    /// <summary>
    /// AGV をパネルへ動かす。全台の経路を計画し直し、パネルごとの通る順番を守らせて走らせる。着いたら <see cref="AgvArrived"/> で知らせる。
    /// </summary>
    /// <param name="neededBy">搬送の必要時刻（計画の順番に使う）。搬送のない移動は null。</param>
    /// <param name="correlationId">一連の流れの ID（例：搬送 ID）。このあとのイベントに付ける。</param>
    public Task<MoveResult> MoveAgvAsync(AgvId id, PanelId destination, DateTimeOffset? neededBy, string correlationId, CancellationToken cancellationToken) =>
        RunAsync(id, correlationId, entry =>
        {
            var travel = entry.Travel;
            if (travel.Position?.NodeId == destination.Value && travel.RemainingSections.Count == 0 && travel.CommittedCommands.Count == 0)
            {
                entry.PendingEvents.Add(metadata => new AgvArrived(metadata, id, destination));
                return MoveResult.AlreadyThere;
            }

            entry.Destination = destination;
            entry.NeededBy = neededBy;
            entry.RequestOrder = ++_requestOrder;
            ReplanAll();

            switch (entry.PlanStatus)
            {
                case PlanStatus.NoRoute:
                    entry.Destination = null;
                    return MoveResult.NoRoute;
                case PlanStatus.Blocked:
                    return MoveResult.Waiting;
                default:
                    return MoveResult.Started;
            }
        }, cancellationToken);

    /// <summary>
    /// キャリアを下げる。高さはマップの設定から読む（設定されていなければ、AGV に測定させる）。
    /// </summary>
    public Task LowerCarrierAsync(AgvId id, string correlationId, CancellationToken cancellationToken) =>
        RunAsync(id, correlationId, entry =>
        {
            var map = CurrentMap();
            var panelId = new PanelId(entry.Travel.Position?.NodeId ?? throw new InvalidOperationException($"{id} は位置がわかりません。"));
            var panel = map.Panels.Single(p => p.Id == panelId);
            entry.Travel.LowerCarrier(panel, mapStore.CarrierHeightOf(panelId));
            return true;
        }, cancellationToken);

    /// <summary>
    /// キャリアを上げる。
    /// </summary>
    public Task RaiseCarrierAsync(AgvId id, string correlationId, CancellationToken cancellationToken) =>
        RunAsync(id, correlationId, entry =>
        {
            entry.Travel.RaiseCarrier();
            return true;
        }, cancellationToken);

    /// <summary>
    /// 緊急停止する。
    /// </summary>
    public Task EmergencyStopAsync(AgvId id, CancellationToken cancellationToken) =>
        RunAsync(id, null, entry =>
        {
            entry.Travel.EmergencyStop();
            return true;
        }, cancellationToken);

    /// <summary>
    /// アラームクリアする。完了の通知を受けたら <see cref="AgvResumed"/> で知らせる。
    /// </summary>
    public Task ClearAlarmAsync(AgvId id, CancellationToken cancellationToken) =>
        RunAsync(id, null, entry =>
        {
            entry.Travel.ClearAlarm();
            return true;
        }, cancellationToken);

    // ---- AGV からのリクエストと通知（AGV との通信の部分から呼ぶ） ----

    /// <summary>登録されている AGV か。</summary>
    internal bool IsRegistered(AgvId id)
    {
        _gate.Wait();
        try
        {
            return _agvs.ContainsKey(id);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 次に状態が変わったときに完了する。コマンドを取りに来た GET が、渡すコマンドができるのを待つのに使う。
    /// 取り逃しのないよう、コマンドを取り出す前に呼んでおく。
    /// </summary>
    internal Task NextChangeAsync(AgvId id)
    {
        _gate.Wait();
        try
        {
            return Entry(id).Changed.Task;
        }
        finally
        {
            _gate.Release();
        }
    }

    // コマンドや位置を取り出すだけの処理では、待っている GET を起こさない（起こすと、取りに来るたびに起き続けるため）
    internal Task<IReadOnlyList<AgvCommand>> HandleCommandRequestAsync(AgvId id, long lastReceived, CancellationToken cancellationToken) =>
        RunAsync(id, null, entry => entry.Travel.OnCommandRequest(lastReceived), cancellationToken, wakesPollers: false);

    internal Task<Pose?> HandlePositionRequestAsync(AgvId id, CancellationToken cancellationToken) =>
        RunAsync(id, null, entry => entry.Travel.OnPositionRequest(), cancellationToken, wakesPollers: false);

    internal Task<NotificationOutcome> HandleMotionReceivedAsync(AgvId id, long number, CancellationToken cancellationToken) =>
        RunAsync(id, null, entry => entry.Travel.OnMotionReceived(number), cancellationToken);

    internal Task<NotificationOutcome> HandleMoveCompletedAsync(
        AgvId id, long number, MoveDirection direction, PanelId arrived, CancellationToken cancellationToken) =>
        RunAsync(id, null, entry => AfterStep(entry, entry.Travel.OnMoveCompleted(number, direction, arrived)), cancellationToken);

    internal Task<NotificationOutcome> HandleRotationCompletedAsync(AgvId id, long number, int reportedHeading, CancellationToken cancellationToken) =>
        RunAsync(id, null, entry => AfterStep(entry, entry.Travel.OnRotationCompleted(number, reportedHeading)), cancellationToken);

    internal Task<NotificationOutcome> HandleCarrierLoweredAsync(AgvId id, long number, CancellationToken cancellationToken) =>
        RunAsync(id, null, entry => Announce(entry, entry.Travel.OnCarrierLowered(number), metadata => new CarrierLowered(metadata, id)), cancellationToken);

    internal Task<NotificationOutcome> HandleCarrierRaisedAsync(AgvId id, long number, CancellationToken cancellationToken) =>
        RunAsync(id, null, entry => Announce(entry, entry.Travel.OnCarrierRaised(number), metadata => new CarrierRaised(metadata, id)), cancellationToken);

    internal Task<NotificationOutcome> HandleCarrierHeightMeasuredAsync(AgvId id, long number, int heightMm, CancellationToken cancellationToken) =>
        RunAsync(id, null, entry =>
        {
            var outcome = entry.Travel.OnCarrierHeightMeasured(number, heightMm);
            if (outcome == NotificationOutcome.Accepted)
            {
                mapStore.SetCarrierHeight(new PanelId(entry.Travel.Position!.NodeId), heightMm, timeProvider.GetUtcNow());
            }

            return outcome;
        }, cancellationToken);

    internal Task<NotificationOutcome> HandleAlarmClearedAsync(AgvId id, long number, CancellationToken cancellationToken) =>
        RunAsync(id, null, entry => RequestReplan(Announce(entry, entry.Travel.OnAlarmCleared(number), metadata => new AgvResumed(metadata, id))), cancellationToken);

    internal Task<NotificationOutcome> HandlePositionResetCompletedAsync(AgvId id, long number, CancellationToken cancellationToken) =>
        RunAsync(id, null, entry => RequestReplan(Announce(entry, entry.Travel.OnPositionResetCompleted(number), metadata => new AgvPositionResetCompleted(metadata, id))), cancellationToken);

    internal Task<NotificationOutcome> HandleAgvErrorAsync(AgvId id, AgvErrorKind kind, CancellationToken cancellationToken) =>
        RunAsync(id, null, entry => entry.Travel.OnAgvError(kind), cancellationToken);

    internal Task HandleBatteryChangedAsync(AgvId id, int percent, CancellationToken cancellationToken) =>
        RunAsync(id, null, entry =>
        {
            entry.PendingEvents.Add(metadata => new AgvBatteryChanged(metadata, id, percent));
            return true;
        }, cancellationToken);

    // ---- 内部 ----

    /// <summary>
    /// AGV ごとの処理を1つずつ順に行い、状態の変わり目（止まった、異常になった）と、処理の中で起きたことをイベントで知らせる。
    /// </summary>
    private async Task<T> RunAsync<T>(
        AgvId id, string? correlationId, Func<AgvEntry, T> action, CancellationToken cancellationToken, bool wakesPollers = true)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entry = Entry(id);
            if (correlationId is not null)
            {
                entry.CorrelationId = correlationId;
            }

            var before = entry.Travel.Status;
            var numberBefore = entry.Travel.NextNumber;
            var result = action(entry);
            var after = entry.Travel.Status;

            // 番号を付けたら、再起動しても続きから数えられるように保存する
            if (entry.Travel.NextNumber != numberBefore)
            {
                _commandNumbers.Save(id, entry.Travel.NextNumber);
            }

            if (after == AgvTravelStatus.EmergencyStopped && before != AgvTravelStatus.EmergencyStopped)
            {
                entry.PendingEvents.Add(metadata => new AgvStopped(metadata, id));
                StartStopTimer(entry);
            }

            if (after != AgvTravelStatus.EmergencyStopped)
            {
                entry.StopTimer?.Dispose();
                entry.StopTimer = null;
                entry.StoppedLong = false;
            }

            if (after == AgvTravelStatus.Error && before != AgvTravelStatus.Error)
            {
                entry.PendingEvents.Add(metadata => new AgvFaulted(metadata, id));

                // 異常になったら、行き先と残りの経路を捨て、障害物にして、すぐに計画を立て直す
                entry.Destination = null;
                entry.PlanStatus = null;
                if (entry.Travel.RemainingSections.Count > 0)
                {
                    entry.Travel.SetRoute([], dispatchNow: false);
                }

                ReplanAll();
            }

            foreach (var create in entry.PendingEvents)
            {
                var metadata = EventMetadata.Start(entry.CorrelationId ?? $"agv:{id}", timeProvider);
                await publisher.PublishAsync(create(metadata), cancellationToken).ConfigureAwait(false);
            }

            entry.PendingEvents.Clear();
            if (wakesPollers)
            {
                if (_replanRequested && _agvs.Values.Any(e => e.PlanStatus == PlanStatus.Blocked || e.WasObstacle))
                {
                    ReplanAll();
                }

                _replanRequested = false;
                DispatchAll();
                entry.NotifyChanged();
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 前進・後退・回転の完了を受け付けたあと、頼まれたパネルに着いて経路を走り終えていたら、着いたことを知らせる。
    /// </summary>
    private NotificationOutcome AfterStep(AgvEntry entry, NotificationOutcome outcome)
    {
        var travel = entry.Travel;
        if (outcome == NotificationOutcome.Accepted
            && entry.Destination is { } destination
            && !travel.HasRoute
            && travel.Position?.NodeId == destination.Value)
        {
            entry.Destination = null;
            entry.PlanStatus = null;
            entry.PendingEvents.Add(metadata => new AgvArrived(metadata, travel.Id, destination));
        }

        if (outcome == NotificationOutcome.Accepted && entry.Destination is null)
        {
            _replanRequested = true; // 着いた（道が空いたかもしれない）
        }

        return outcome;
    }

    /// <summary>止まっていた AGV が再開した（道が空いたかもしれない）ので、塞がれて待っている AGV がいれば計画を立て直す。</summary>
    private NotificationOutcome RequestReplan(NotificationOutcome outcome)
    {
        if (outcome == NotificationOutcome.Accepted)
        {
            _replanRequested = true;
        }

        return outcome;
    }

    // ---- 全台の経路の計画 ----

    /// <summary>
    /// 全台の経路を計画し直し、各 AGV のまだ渡していない区間と、パネルごとの通る順番を入れ替える。
    /// </summary>
    /// <remarks>
    /// 計画の順番は、搬送の必要時刻が早い AGV から（同じなら頼まれた順）。搬送のない移動はそのあと、行き先のない AGV は最後。
    /// 位置リセットを待っている AGV は、動かせない障害物にする。キャリアが下がっている AGV は、行き先がない（止まり続ける）ものとする。
    /// </remarks>
    private void ReplanAll()
    {
        var map = PlanningMapFactory.FromMap(CurrentMap());
        var obstacles = new List<PlanningObstacle>();
        var participants = new List<AgvEntry>();
        foreach (var entry in _agvs.Values.Where(e => e.Travel.Position is not null))
        {
            entry.WasObstacle = false;
            if (IsObstacle(entry))
            {
                entry.WasObstacle = entry.Travel.Status != AgvTravelStatus.ResettingPosition;
                var occupied = _reservations.ReservedBy(entry.Travel.Id).Select(p => p.Value).DefaultIfEmpty(entry.Travel.Position!.NodeId).ToArray();
                obstacles.Add(new PlanningObstacle(entry.Travel.Id.Value, occupied));
            }
            else
            {
                participants.Add(entry);
            }
        }

        var ordered = participants
            .OrderBy(e => e.Destination is null || e.Travel.Carrier != CarrierState.Raised)
            .ThenBy(e => e.NeededBy is null)
            .ThenBy(e => e.NeededBy)
            .ThenBy(e => e.RequestOrder)
            .ThenBy(e => e.Travel.Id.Value, StringComparer.Ordinal)
            .ToArray();
        var request = new PlanningRequest(map, ordered.Select(ToPlanningAgent).ToArray(), obstacles, _planning.ImprovementBudget, _planning.Horizon);
        var result = _fleetPlanner.Plan(request);

        // 全台の経路を入れ替えてから、まとめて区間を渡す（途中で渡すと、ほかの AGV の古い経路で順番を判断してしまうため）
        _passingGate!.Update(result.PassingOrder);
        foreach (var (entry, plan) in ordered.Zip(result.Plans))
        {
            entry.PlanStatus = entry.Destination is null ? null : plan.Status;
            var travel = entry.Travel;
            IReadOnlyList<TravelSection> sections = plan.Status == PlanStatus.Planned
                ? TravelSectionBuilder.Build(travel.PlanningStart, ToRouteSteps(travel.PlanningStart, plan.Actions.Skip(travel.CommittedCommands.Count)))
                : [];
            if (sections.Count > 0 || travel.RemainingSections.Count > 0)
            {
                travel.SetRoute(sections, dispatchNow: false);
            }
        }

        DispatchAll();
    }

    /// <summary>区間を渡せる AGV に、次の区間を渡す。</summary>
    private void DispatchAll()
    {
        foreach (var entry in _agvs.Values)
        {
            entry.Travel.TryDispatchNext();
        }
    }

    private PlanningAgent ToPlanningAgent(AgvEntry entry)
    {
        var travel = entry.Travel;
        var pose = travel.Position!;
        var committed = new List<AgentAction>();
        var node = pose.NodeId;
        foreach (var step in travel.CommittedCommands)
        {
            switch (step)
            {
                case ForwardCommand forward:
                    committed.Add(new MoveAction(AgentDirection.Forward, node, forward.To.Value, forward.DistanceMm));
                    node = forward.To.Value;
                    break;
                case BackwardCommand backward:
                    committed.Add(new MoveAction(AgentDirection.Backward, node, backward.To.Value, backward.DistanceMm));
                    node = backward.To.Value;
                    break;
                case RotateCommand rotate:
                    committed.Add(new RotateAction(node, rotate.Heading));
                    break;
            }
        }

        var goal = entry.Destination is { } destination && travel.Carrier == CarrierState.Raised ? new AgentGoal(destination.Value, Heading: null) : null;
        var motion = entry.Motion;
        return new PlanningAgent(
            travel.Id.Value,
            new AgentPose(pose.NodeId, pose.Heading),
            new AgentMotion(motion.ForwardSpeedMmPerSecond, motion.BackwardSpeedMmPerSecond, motion.RotationSpeedDegreesPerSecond),
            goal,
            committed,
            Movable: IsMovable(entry));
    }

    /// <summary>
    /// 計画で動かせない障害物にする AGV：位置リセット待ち、異常、緊急停止が長引いた（設定の時間を過ぎた）もの。
    /// 障害物は、予約しているパネル（止まった区間）を塞ぐ。
    /// </summary>
    private static bool IsObstacle(AgvEntry entry) => entry.Travel.Status switch
    {
        AgvTravelStatus.ResettingPosition or AgvTravelStatus.Error => true,
        AgvTravelStatus.EmergencyStopped => entry.StoppedLong,
        _ => false,
    };

    /// <summary>
    /// 行き先がなく、通り道を塞いでいたら待機パネルへ動かしてよいか（動かせる状況か）。
    /// 待機中で、キャリアが上がっていて、充電ピンのあるパネル（充電中）にいないこと。
    /// </summary>
    private bool IsMovable(AgvEntry entry)
    {
        var travel = entry.Travel;
        return entry.Destination is null
            && travel.Status == AgvTravelStatus.Idle
            && travel.Carrier == CarrierState.Raised
            && !CurrentMap().Panels.Any(panel => panel.Id.Value == travel.Position!.NodeId && panel.HasChargingPin);
    }

    /// <summary>
    /// 緊急停止になったら、設定の時間（既定1分）たっても再開しなければ、障害物にして計画を立て直す。
    /// </summary>
    private void StartStopTimer(AgvEntry entry)
    {
        entry.StopTimer?.Dispose();
        entry.StopTimer = timeProvider.CreateTimer(_ => _ = OnStoppedLongAsync(entry), null, _planning.EmergencyStopReplanDelay, Timeout.InfiniteTimeSpan);
    }

    private async Task OnStoppedLongAsync(AgvEntry entry)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (entry.Travel.Status != AgvTravelStatus.EmergencyStopped || entry.StoppedLong)
            {
                return;
            }

            entry.StoppedLong = true;
            ReplanAll();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>計画の動作を、区間を組み立てるための経路の動作に変える。</summary>
    private static List<RouteStep> ToRouteSteps(Pose start, IEnumerable<TimedAction> actions)
    {
        var steps = new List<RouteStep>();
        var pose = start;
        foreach (var action in actions.Select(a => a.Action))
        {
            switch (action)
            {
                case MoveAction move:
                    pose = new Pose(move.To, pose.Heading);
                    steps.Add(new MoveStep(move.Direction == AgentDirection.Forward ? MoveDirection.Forward : MoveDirection.Backward, move.From, move.To, move.DistanceMm, pose));
                    break;
                case RotateAction rotate:
                    pose = new Pose(rotate.NodeId, rotate.ToHeading);
                    steps.Add(new RotateStep(rotate.NodeId, rotate.ToHeading, pose));
                    break;
            }
        }

        return steps;
    }

    private static NotificationOutcome Announce(AgvEntry entry, NotificationOutcome outcome, Func<EventMetadata, IIntegrationEvent> create)
    {
        if (outcome == NotificationOutcome.Accepted)
        {
            entry.PendingEvents.Add(create);
        }

        return outcome;
    }

    private RouteResult Plan(AgvEntry entry, PanelId destination)
    {
        var graph = RouteGraphFactory.FromMap(CurrentMap());
        return _planner.Plan(new RouteRequest(graph, entry.Travel.Position!, destination.Value, GoalHeading: null, entry.Motion));
    }

    private MapSnapshot CurrentMap() =>
        mapStore.Current ?? throw new InvalidOperationException("マップがまだ公開されていません。");

    private AgvEntry Entry(AgvId id) =>
        _agvs.TryGetValue(id, out var entry) ? entry : throw new InvalidOperationException($"{id} は登録されていません。");

    private static AgvState ToState(AgvTravelStatus status) => status switch
    {
        AgvTravelStatus.Unregistered => AgvState.Unregistered,
        AgvTravelStatus.ResettingPosition => AgvState.ResettingPosition,
        AgvTravelStatus.Idle => AgvState.Idle,
        AgvTravelStatus.Moving => AgvState.Moving,
        AgvTravelStatus.EmergencyStopped => AgvState.EmergencyStopped,
        _ => AgvState.Error,
    };

    /// <summary>
    /// 登録した AGV ごとの情報。
    /// </summary>
    private sealed class AgvEntry(AgvTravel travel, MotionProfile motion)
    {
        public AgvTravel Travel { get; } = travel;

        public MotionProfile Motion { get; } = motion;

        /// <summary>今の一連の流れの ID（配車からの頼みに付いてきたもの）。</summary>
        public string? CorrelationId { get; set; }

        /// <summary>頼まれた行き先。着いたら null。</summary>
        public PanelId? Destination { get; set; }

        /// <summary>頼まれた搬送の必要時刻（計画の順番に使う）。搬送のない移動なら null。</summary>
        public DateTimeOffset? NeededBy { get; set; }

        /// <summary>頼まれた順（同じ必要時刻のときの順番に使う）。</summary>
        public long RequestOrder { get; set; }

        /// <summary>行き先への計画の結果。行き先がなければ null。</summary>
        public PlanStatus? PlanStatus { get; set; }

        /// <summary>前の計画で、止まっているので障害物にした（再開したら、計画を立て直す）。</summary>
        public bool WasObstacle { get; set; }

        /// <summary>緊急停止が、設定の時間を過ぎて長引いている（障害物にする）。</summary>
        public bool StoppedLong { get; set; }

        /// <summary>緊急停止が長引いたかを測るタイマー。</summary>
        public ITimer? StopTimer { get; set; }

        /// <summary>処理の中で起きたこと。処理の終わりにイベントとして公開する。</summary>
        public List<Func<EventMetadata, IIntegrationEvent>> PendingEvents { get; } = [];

        /// <summary>
        /// 状態が変わった（渡すコマンドができたかもしれない）ことを、コマンドを取りに来て待っている GET に知らせる。
        /// </summary>
        public TaskCompletionSource Changed { get; private set; } = NewSignal();

        public void NotifyChanged()
        {
            var changed = Changed;
            Changed = NewSignal();
            changed.TrySetResult();
        }

        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
