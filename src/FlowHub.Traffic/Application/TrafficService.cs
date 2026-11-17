using FlowHub.BuildingBlocks;
using FlowHub.Map;
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
public sealed class TrafficService(IMapStore mapStore, IEventPublisher publisher, TimeProvider timeProvider) : ITrafficService, IDisposable
{
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
            if (!_agvs.TryAdd(id, new AgvEntry(new AgvTravel(id, _reservations), motion)))
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
    /// AGV をパネルへ動かす。着いたら <see cref="AgvArrived"/> で知らせる。
    /// </summary>
    /// <param name="correlationId">一連の流れの ID（例：搬送 ID）。このあとのイベントに付ける。</param>
    public Task<MoveResult> MoveAgvAsync(AgvId id, PanelId destination, string correlationId, CancellationToken cancellationToken) =>
        RunAsync(id, correlationId, entry =>
        {
            var travel = entry.Travel;
            if (travel.Position?.NodeId == destination.Value)
            {
                entry.PendingEvents.Add(metadata => new AgvArrived(metadata, id, destination));
                return MoveResult.AlreadyThere;
            }

            var route = Plan(entry, destination);
            if (!route.Found)
            {
                return MoveResult.NoRoute;
            }

            var result = travel.StartRoute(TravelSectionBuilder.Build(travel.Position!, route.Steps));
            if (!result.Succeeded)
            {
                return MoveResult.Waiting;
            }

            entry.Destination = destination;
            return MoveResult.Started;
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

    internal Task<IReadOnlyList<AgvCommand>> HandleCommandRequestAsync(AgvId id, long lastReceived, CancellationToken cancellationToken) =>
        RunAsync(id, null, entry => entry.Travel.OnCommandRequest(lastReceived), cancellationToken);

    internal Task<Pose?> HandlePositionRequestAsync(AgvId id, CancellationToken cancellationToken) =>
        RunAsync(id, null, entry => entry.Travel.OnPositionRequest(), cancellationToken);

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
        RunAsync(id, null, entry => Announce(entry, entry.Travel.OnAlarmCleared(number), metadata => new AgvResumed(metadata, id)), cancellationToken);

    internal Task<NotificationOutcome> HandlePositionResetCompletedAsync(AgvId id, long number, CancellationToken cancellationToken) =>
        RunAsync(id, null, entry => Announce(entry, entry.Travel.OnPositionResetCompleted(number), metadata => new AgvPositionResetCompleted(metadata, id)), cancellationToken);

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
    private async Task<T> RunAsync<T>(AgvId id, string? correlationId, Func<AgvEntry, T> action, CancellationToken cancellationToken)
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
            var result = action(entry);
            var after = entry.Travel.Status;

            if (after == AgvTravelStatus.EmergencyStopped && before != AgvTravelStatus.EmergencyStopped)
            {
                entry.PendingEvents.Add(metadata => new AgvStopped(metadata, id));
            }

            if (after == AgvTravelStatus.Error && before != AgvTravelStatus.Error)
            {
                entry.PendingEvents.Add(metadata => new AgvFaulted(metadata, id));
            }

            foreach (var create in entry.PendingEvents)
            {
                var metadata = EventMetadata.Start(entry.CorrelationId ?? $"agv:{id}", timeProvider);
                await publisher.PublishAsync(create(metadata), cancellationToken).ConfigureAwait(false);
            }

            entry.PendingEvents.Clear();
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
    private static NotificationOutcome AfterStep(AgvEntry entry, NotificationOutcome outcome)
    {
        var travel = entry.Travel;
        if (outcome == NotificationOutcome.Accepted
            && entry.Destination is { } destination
            && !travel.HasRoute
            && travel.Position?.NodeId == destination.Value)
        {
            entry.Destination = null;
            entry.PendingEvents.Add(metadata => new AgvArrived(metadata, travel.Id, destination));
        }

        return outcome;
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

        /// <summary>処理の中で起きたこと。処理の終わりにイベントとして公開する。</summary>
        public List<Func<EventMetadata, IIntegrationEvent>> PendingEvents { get; } = [];
    }
}
