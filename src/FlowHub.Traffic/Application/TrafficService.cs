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
/// <param name="controlStatuses">コントロールステータスの保存場所。省略したら、メモリーの中にだけ持つ（オートから始める）。</param>
public sealed class TrafficService(
    IMapStore mapStore,
    IEventPublisher publisher,
    TimeProvider timeProvider,
    ICommandNumberStore? commandNumbers = null,
    TrafficPlanningOptions? planning = null,
    IControlStatusStore? controlStatuses = null) : ITrafficService, IDisposable
{
    private readonly ICommandNumberStore _commandNumbers = commandNumbers ?? new InMemoryCommandNumberStore();
    private readonly IControlStatusStore _controlStatuses = controlStatuses ?? new InMemoryControlStatusStore();
    private readonly TrafficPlanningOptions _planning = planning ?? new TrafficPlanningOptions();
    private readonly PrioritizedPlanner _fleetPlanner = new(timeProvider);
    private PassingOrderGate? _passingGate;
    private DelegateTurnGate? _turnGate;
    private long _requestOrder;

    /// <summary>塞がれて待っている AGV がいるかもしれない変化（着いた、再開した）があり、計画を立て直す必要がある。</summary>
    private bool _replanRequested;

    /// <summary>マニュアルの1つずつの動作で、予約しているパネルが変わった（ほかの AGV の計画を立て直す）。</summary>
    private bool _manualStepsChanged;

    /// <summary>通り道を塞いでいる行き先のない AGV を、待機パネルへどかしてよいか。</summary>
    private bool _evacuationEnabled = true;

    /// <summary>タイマーなど、リクエストの外で状態を変えたあとに呼ぶ後始末（組み立てが渡す。なければ呼ばない）。</summary>
    private Func<CancellationToken, Task>? _afterBackgroundWork;

    private readonly SemaphoreSlim _gate = new(1, 1);

    public void Dispose() => _gate.Dispose();
    private readonly PanelReservations _reservations = new();
    private readonly Dictionary<AgvId, AgvEntry> _agvs = [];
    private readonly RoutePlanner _planner = new();

    // ---- AGV の登録と参照 ----

    /// <summary>
    /// AGV を登録する。速さは移動コストの計算に使う（AGV の種類ごとの設定から渡す）。
    /// コントロールステータスは、保存場所から読む（再起動しても残す）。
    /// </summary>
    /// <param name="canTravelSpecialTransfer">特殊消耗品受け渡しパネルを走れるか（AGV の種類ごとの設定。既定は走れない）。</param>
    public void RegisterAgv(AgvId id, MotionProfile motion, bool canTravelSpecialTransfer = false)
    {
        ArgumentNullException.ThrowIfNull(motion);
        _gate.Wait();
        try
        {
            _passingGate ??= new PassingOrderGate(agv => _agvs.TryGetValue(agv, out var entry) ? entry.Travel.RemainingRoutePanels : null);

            // マニュアルの1つずつの動作は、全台の計画の通る順番には従わず、予約だけで守る
            _turnGate ??= new DelegateTurnGate((agv, panels) => (_agvs.TryGetValue(agv, out var entry) && IsManualStepping(entry)) || _passingGate.IsMyTurn(agv, panels));
            var entry = new AgvEntry(new AgvTravel(id, _reservations, _commandNumbers.NextNumberOf(id), _turnGate), motion)
            {
                Control = _controlStatuses.StatusOf(id),
                CanTravelSpecialTransfer = canTravelSpecialTransfer,
            };
            if (!_agvs.TryAdd(id, entry))
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
            return ViewOf(id, Entry(id));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 登録しているすべての AGV の今の様子を、ID の順に返す（画面に出すため）。同じ時点の様子をそろえて読む。
    /// </summary>
    public IReadOnlyList<AgvView> ListAgvs()
    {
        _gate.Wait();
        try
        {
            return [.. _agvs.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal).Select(pair => ViewOf(pair.Key, pair.Value))];
        }
        finally
        {
            _gate.Release();
        }
    }

    private static AgvView ViewOf(AgvId id, AgvEntry entry)
    {
        var travel = entry.Travel;
        return new AgvView(id, travel.Position, ToState(travel.Status), travel.Carrier == CarrierState.Raised, travel.CumulativeDistanceMm, entry.Control)
        {
            StopsAt = travel.Position is null ? null : travel.PlanningStart,
            CanTravelSpecialTransfer = entry.CanTravelSpecialTransfer,
            CarrierMoving = travel.Carrier is CarrierState.Lowering or CarrierState.Raising,
            BatteryPercent = entry.BatteryPercent,
        };
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

    /// <summary>
    /// タイマーなど、リクエストの外で状態を変えたあとに呼ぶ後始末をつなぐ（組み立てが、イベントを届ける処理を渡す）。
    /// </summary>
    public void AttachAfterBackgroundWork(Func<CancellationToken, Task> afterBackgroundWork)
    {
        ArgumentNullException.ThrowIfNull(afterBackgroundWork);
        _afterBackgroundWork = afterBackgroundWork;
    }

    // ---- 配車などからの頼み ----

    /// <summary>
    /// 通り道を塞いでいる行き先のない AGV を、待機パネルへどかしてよいかを切り替える。切り替えたら、全台の経路を計画し直す。
    /// </summary>
    public void SetEvacuationEnabled(bool enabled)
    {
        _gate.Wait();
        try
        {
            if (_evacuationEnabled == enabled)
            {
                return;
            }

            _evacuationEnabled = enabled;
            if (_passingGate is not null)
            {
                ReplanAll();
            }

            foreach (var entry in _agvs.Values)
            {
                entry.NotifyChanged(); // 渡すコマンドができたかもしれない
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 位置を上書きする（最初の登録も同じ）。指定のパネルをほかの AGV が予約していたら false。
    /// </summary>
    /// <exception cref="InvalidOperationException">切り離し中のとき（切り離しから切り替える操作で、位置を指定する）。</exception>
    public Task<bool> ResetPositionAsync(AgvId id, Pose position, CancellationToken cancellationToken) =>
        RunAsync(id, null, entry =>
        {
            RefuseWhenDetached(entry);
            return entry.Travel.ResetPosition(position).Succeeded;
        }, cancellationToken);

    /// <summary>
    /// コントロールステータスを切り替える。切り替えたら、全台の経路を計画し直す。
    /// </summary>
    /// <remarks>
    /// オートと割り当て停止へは、いつでも切り替えられる。マニュアルとロックへは、待機中か緊急停止中のときだけで、行き先と残りの経路を捨てる。
    /// 切り離しへは、ロック中のときだけで、予約を外す。切り離しからは、位置を指定してもらい、位置リセットの完了の通知を受けたら切り替わる。
    /// </remarks>
    public Task<bool> ChangeControlStatusAsync(AgvId id, ControlStatus target, Pose? position, CancellationToken cancellationToken) =>
        RunAsync(id, null, entry =>
        {
            if (entry.Control == ControlStatus.Detached)
            {
                return LeaveDetached(entry, target, position);
            }

            switch (target)
            {
                case ControlStatus.Manual or ControlStatus.Locked:
                    if (entry.Travel.Status is not (AgvTravelStatus.Idle or AgvTravelStatus.EmergencyStopped))
                    {
                        throw new InvalidOperationException($"{id} は走っていないとき（待機中か緊急停止中）でないため、{target} に切り替えられません（今の状態：{entry.Travel.Status}）。");
                    }

                    // 緊急停止中で行き先が残っていたら、行き先と残りの経路を捨てる（システムの判断では、もう動かさないため）
                    DropDestination(entry);
                    break;
                case ControlStatus.Detached:
                    if (entry.Control != ControlStatus.Locked)
                    {
                        throw new InvalidOperationException($"{id} はロック中でないため、切り離しに切り替えられません。");
                    }

                    _reservations.ReleaseAll(id);
                    break;
            }

            SetControl(entry, target);
            ReplanAll();
            return true;
        }, cancellationToken);

    /// <summary>コントロールステータスを変え、再起動しても残るように保存する。</summary>
    private void SetControl(AgvEntry entry, ControlStatus status)
    {
        entry.Control = status;
        _controlStatuses.Save(entry.Travel.Id, status);
    }

    /// <summary>切り離しから切り替える。位置リセットを送り、完了の通知を受けたら切り替わる。</summary>
    private static bool LeaveDetached(AgvEntry entry, ControlStatus target, Pose? position)
    {
        if (target == ControlStatus.Detached)
        {
            return true;
        }

        if (position is null)
        {
            throw new ArgumentException($"{entry.Travel.Id} を切り離しから切り替えるには、位置（パネルと絶対角度）を指定してください。", nameof(position));
        }

        if (!entry.Travel.ResetPosition(position).Succeeded)
        {
            return false;
        }

        entry.PendingControl = target;
        return true;
    }

    /// <summary>
    /// AGV をパネルへ動かす。全台の経路を計画し直し、パネルごとの通る順番を守らせて走らせる。着いたら <see cref="AgvArrived"/> で知らせる。
    /// </summary>
    /// <param name="neededBy">搬送の必要時刻（計画の順番に使う）。搬送のない移動は null。</param>
    /// <param name="correlationId">一連の流れの ID（例：搬送 ID）。このあとのイベントに付ける。</param>
    public Task<MoveResult> MoveAgvAsync(AgvId id, PanelId destination, DateTimeOffset? neededBy, string correlationId, CancellationToken cancellationToken) =>
        RunAsync(id, correlationId, entry =>
        {
            RefuseUnlessSystemControlled(entry);
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
    /// AGV を止める（搬送の取りやめなど）。行き先とまだ渡していないコマンドを取り下げ、AGV が受け取ったコマンドを終えたところで止まる。
    /// 止まったパネル以外の予約を外して、全台の経路を計画し直す。着いたことは知らせない。
    /// </summary>
    /// <exception cref="InvalidOperationException">システムが動かす AGV（オートか割り当て停止）でないとき。</exception>
    public Task StopAgvAsync(AgvId id, string correlationId, CancellationToken cancellationToken) =>
        RunAsync(id, correlationId, entry =>
        {
            RefuseUnlessSystemControlled(entry);
            entry.Destination = null;
            entry.PlanStatus = null;
            entry.Travel.CancelRoute();
            ReplanAll();
            return true;
        }, cancellationToken);

    /// <summary>
    /// キャリアを下げる。高さはマップの設定から読む（設定されていなければ、AGV に測定させる）。
    /// </summary>
    public Task LowerCarrierAsync(AgvId id, string correlationId, CancellationToken cancellationToken) =>
        RunAsync(id, correlationId, entry =>
        {
            RefuseUnlessSystemControlled(entry);
            LowerCarrier(entry);
            return true;
        }, cancellationToken);

    private void LowerCarrier(AgvEntry entry)
    {
        var panelId = new PanelId(entry.Travel.Position?.NodeId ?? throw new InvalidOperationException($"{entry.Travel.Id} は位置がわかりません。"));
        var panel = CurrentMap().Panels.Single(p => p.Id == panelId);
        entry.Travel.LowerCarrier(panel, mapStore.CarrierHeightOf(panelId));
    }

    /// <summary>
    /// キャリアを上げる。
    /// </summary>
    public Task RaiseCarrierAsync(AgvId id, string correlationId, CancellationToken cancellationToken) =>
        RunAsync(id, correlationId, entry =>
        {
            RefuseUnlessSystemControlled(entry);
            entry.Travel.RaiseCarrier();
            return true;
        }, cancellationToken);

    /// <summary>
    /// 緊急停止する。切り離し中のほかは、どのステータスでも受け付ける。
    /// </summary>
    public Task EmergencyStopAsync(AgvId id, CancellationToken cancellationToken) =>
        RunAsync(id, null, entry =>
        {
            RefuseWhenDetached(entry);
            entry.Travel.EmergencyStop();
            return true;
        }, cancellationToken);

    /// <summary>
    /// アラームクリアする。完了の通知を受けたら <see cref="AgvResumed"/> で知らせる。ロック中と切り離し中は受け付けない。
    /// </summary>
    public Task ClearAlarmAsync(AgvId id, CancellationToken cancellationToken) =>
        RunAsync(id, null, entry =>
        {
            if (entry.Control is ControlStatus.Locked or ControlStatus.Detached)
            {
                throw new InvalidOperationException($"{id} は {entry.Control} のため、アラームクリアを受け付けません。");
            }

            entry.Travel.ClearAlarm();
            return true;
        }, cancellationToken);

    /// <summary>システムの判断で動かすコマンド（移動、キャリアの上げ下げ）は、オートか割り当て停止のときだけ受け付ける。</summary>
    private static void RefuseUnlessSystemControlled(AgvEntry entry)
    {
        if (!IsSystemControlled(entry))
        {
            throw new InvalidOperationException($"{entry.Travel.Id} は {entry.Control} のため、システムの判断では動かせません。");
        }
    }

    private static void RefuseWhenDetached(AgvEntry entry)
    {
        if (entry.Control == ControlStatus.Detached)
        {
            throw new InvalidOperationException($"{entry.Travel.Id} は切り離し中のため、コマンドを受け付けません。");
        }
    }

    private static bool IsSystemControlled(AgvEntry entry) => entry.Control is ControlStatus.Auto or ControlStatus.AssignmentPaused;

    // ---- マニュアルのコマンド（画面から） ----

    /// <summary>
    /// マニュアルで、行き先のパネルを指定して走らせる。経路はシステムが求め、全台の計画で一番先に計画する。
    /// 今はほかの AGV に塞がれていて行けないときと、マップの上に経路がないときは、受け付けない。着いても、着いたことは知らせない。
    /// </summary>
    /// <param name="arrival">
    /// 着いたときの向き（0・90・180・270度のどれか）。指定しなければ null で、向きは問わない。
    /// 今いるパネルで向きだけが違えば、その場で向きを合わせる。特殊消耗品受け渡しパネルでは、キャリアを下ろす角度と違う向きは受け付けない。
    /// </param>
    /// <exception cref="InvalidOperationException">マニュアルでないとき。待機中でない（走っている途中、緊急停止中など）とき。キャリアが上がっていないとき。</exception>
    /// <exception cref="ArgumentOutOfRangeException">特殊消耗品受け渡しパネルでないのに、着いたときの向きが 0・90・180・270度のどれでもないとき。</exception>
    public Task<ManualMoveResult> ManualMoveAsync(AgvId id, PanelId destination, Heading? arrival, CancellationToken cancellationToken) =>
        RunAsync(id, $"manual:{id}", entry =>
        {
            RequireManual(entry);
            var travel = entry.Travel;
            if (travel.Status != AgvTravelStatus.Idle || entry.Destination is not null)
            {
                throw new InvalidOperationException($"{id} は待機中でないため、行き先を受け付けません（今の状態：{travel.Status}）。");
            }

            var arrivalHeading = arrival?.Degrees;
            if (arrivalHeading is not null)
            {
                if (SpecialPanelHeadings().TryGetValue(destination.Value, out var lowering))
                {
                    if (lowering != arrivalHeading)
                    {
                        return ManualMoveResult.ArrivalHeadingNotAllowed;
                    }
                }
                else if (arrivalHeading is not (0 or 90 or 180 or 270))
                {
                    throw new ArgumentOutOfRangeException(nameof(arrival), arrivalHeading, "着いたときの向きは、0・90・180・270度のどれかにしてください（ラインを読み取って回るため）。");
                }
            }

            if (travel.Position!.NodeId == destination.Value && (arrivalHeading is null || travel.Position.Heading == arrivalHeading))
            {
                return ManualMoveResult.AlreadyThere;
            }

            if (travel.Carrier != CarrierState.Raised)
            {
                throw new InvalidOperationException($"{id} はキャリアが上がっていないため、走らせられません。");
            }

            entry.SetManualDestination(destination, arrivalHeading);
            entry.NeededBy = null;
            entry.RequestOrder = ++_requestOrder;
            ReplanAll();

            switch (entry.PlanStatus)
            {
                case PlanStatus.NoRoute:
                    DropDestination(entry);
                    return ManualMoveResult.NoRoute;
                case PlanStatus.Blocked:
                    DropDestination(entry);
                    ReplanAll(); // 待たないので、行き先のない AGV として計画し直す
                    return ManualMoveResult.Blocked;
                default:
                    return ManualMoveResult.Started;
            }
        }, cancellationToken);

    /// <summary>
    /// マニュアルで、1つずつの動作を送る。前の動作が終わる前なら、後ろに並べる（渡すときに予約が取れなければ、そこで止めて残りを捨て、
    /// <see cref="ManualStepsDropped"/> で知らせる）。全台の計画の通る順番には従わず、予約だけで守る。
    /// </summary>
    /// <exception cref="InvalidOperationException">マニュアルでないとき。行き先へ走っている途中や、緊急停止中などのとき。キャリアが上がっていないとき。</exception>
    public Task<ManualStepResult> ManualStepAsync(AgvId id, ManualStep step, CancellationToken cancellationToken) =>
        RunAsync(id, $"manual:{id}", entry =>
        {
            ArgumentNullException.ThrowIfNull(step);
            RequireManual(entry);
            var travel = entry.Travel;
            if (entry.Destination is not null || travel.Status is not (AgvTravelStatus.Idle or AgvTravelStatus.Moving))
            {
                throw new InvalidOperationException($"{id} は行き先へ走っている途中か、待機中でないため、1つずつの動作を受け付けません（今の状態：{travel.Status}）。");
            }

            if (ManualStepBuilder.Build(CurrentMap(), travel.RouteEnd, step, entry.CanTravelSpecialTransfer) is not { } section)
            {
                return ManualStepResult.NotAllowed;
            }

            var queued = travel.HasRoute;
            travel.AppendSection(section);
            if (!queued && !travel.TryDispatchNext())
            {
                travel.SetRoute([], dispatchNow: false); // すぐに渡せるのに予約が取れない
                return ManualStepResult.Blocked;
            }

            _manualStepsChanged = true;
            return ManualStepResult.Accepted;
        }, cancellationToken);

    /// <summary>マニュアルで、キャリアを下げる。高さはマップの設定から読む（設定されていなければ、AGV に測定させる）。</summary>
    /// <exception cref="InvalidOperationException">マニュアルでないとき。</exception>
    public Task ManualLowerCarrierAsync(AgvId id, CancellationToken cancellationToken) =>
        RunAsync(id, $"manual:{id}", entry =>
        {
            RequireManual(entry);
            LowerCarrier(entry);
            return true;
        }, cancellationToken);

    /// <summary>マニュアルで、キャリアを上げる。</summary>
    /// <exception cref="InvalidOperationException">マニュアルでないとき。</exception>
    public Task ManualRaiseCarrierAsync(AgvId id, CancellationToken cancellationToken) =>
        RunAsync(id, $"manual:{id}", entry =>
        {
            RequireManual(entry);
            entry.Travel.RaiseCarrier();
            return true;
        }, cancellationToken);

    private static void RequireManual(AgvEntry entry)
    {
        if (entry.Control != ControlStatus.Manual)
        {
            throw new InvalidOperationException($"{entry.Travel.Id} はマニュアルでないため、マニュアルのコマンドを受け付けません（今のステータス：{entry.Control}）。");
        }
    }

    /// <summary>マニュアルで、1つずつの動作で動かしている（行き先がない）。全台の計画では障害物にし、通る順番には従わせない。</summary>
    private static bool IsManualStepping(AgvEntry entry) => entry.Control == ControlStatus.Manual && entry.Destination is null;

    /// <summary>マニュアルで、行き先を指定して走らせている。全台の計画で一番先に計画する。</summary>
    private static bool IsManualMoving(AgvEntry entry) => entry.Control == ControlStatus.Manual && entry.Destination is not null;

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
        RunAsync(id, null, entry => IsDetachedForGood(entry) ? Array.Empty<AgvCommand>() : entry.Travel.OnCommandRequest(lastReceived), cancellationToken, wakesPollers: false);

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
        RunAsync(id, null, entry =>
        {
            var outcome = entry.Travel.OnPositionResetCompleted(number);
            if (outcome == NotificationOutcome.Accepted && entry.PendingControl is { } pending)
            {
                // 切り離しから切り替える位置リセットが済んだので、切り替える
                SetControl(entry, pending);
                entry.PendingControl = null;
            }

            return RequestReplan(Announce(entry, outcome, metadata => new AgvPositionResetCompleted(metadata, id)));
        }, cancellationToken);

    internal Task<NotificationOutcome> HandleAgvErrorAsync(AgvId id, AgvErrorKind kind, CancellationToken cancellationToken) =>
        RunAsync(id, null, entry => entry.Travel.OnAgvError(kind), cancellationToken);

    internal Task HandleBatteryChangedAsync(AgvId id, int percent, CancellationToken cancellationToken) =>
        RunAsync(id, null, entry =>
        {
            entry.BatteryPercent = percent;
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

            var start = BeginChange();
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
                DropDestination(entry);
                ReplanAll();
            }

            await CompleteChangeAsync(start, entry, dispatches: wakesPollers, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 処理の前の様子。後始末で、処理の中で変わったもの（予約を外した充電ポート、新しいコマンドができた AGV）を見つけるのに使う。
    /// </summary>
    private sealed record ChangeStart(HashSet<PanelId> ReservedChargingPorts, IReadOnlyDictionary<AgvId, long> NextNumbers);

    /// <summary>処理を始める前の様子を覚える（ロックの中で呼ぶ）。</summary>
    private ChangeStart BeginChange() =>
        new(ReservedChargingPorts(), _agvs.ToDictionary(pair => pair.Key, pair => pair.Value.Travel.NextNumber));

    /// <summary>
    /// 処理のあとの後始末（ロックの中で呼ぶ）。ロックを取る入り口は、すべてここを通す。
    /// 処理の中で起きたことをイベントで知らせ、区間を渡せる AGV に渡し、待っているコマンドの要求を起こし、空いた充電ポートを知らせる。
    /// </summary>
    /// <param name="actor">処理を頼まれた AGV（タイマーなど、AGV の頼みでないときは null）。</param>
    /// <param name="dispatches">区間を渡すか（コマンドや位置を取り出すだけの処理では、渡さない）。</param>
    private async Task CompleteChangeAsync(ChangeStart start, AgvEntry? actor, bool dispatches, CancellationToken cancellationToken)
    {
        foreach (var entry in _agvs.Values)
        {
            foreach (var create in entry.PendingEvents)
            {
                var metadata = EventMetadata.Start(entry.CorrelationId ?? $"agv:{entry.Travel.Id}", timeProvider);
                await publisher.PublishAsync(create(metadata), cancellationToken).ConfigureAwait(false);
            }

            entry.PendingEvents.Clear();
        }

        if (dispatches)
        {
            // マニュアルの1つずつの動作で予約するパネルが変わったら、ほかの AGV はそれを避けて計画し直す
            if ((_replanRequested && _agvs.Values.Any(e => e.PlanStatus == PlanStatus.Blocked || e.WasObstacle)) || _manualStepsChanged)
            {
                ReplanAll();
            }

            _replanRequested = false;
            _manualStepsChanged = false;
            DispatchAll();
            await DropBlockedManualStepsAsync(cancellationToken).ConfigureAwait(false);
        }

        // コマンドを待っている要求を起こす。頼まれた AGV は、区間を渡す処理のときに起こす
        // （コマンドや位置を取り出すだけの処理で起こすと、取りに来るたびに起き続けるため）。ほかの AGV は、新しいコマンドができたときに起こす
        foreach (var (id, entry) in _agvs)
        {
            var gotCommand = start.NextNumbers.TryGetValue(id, out var numberBefore) && entry.Travel.NextNumber != numberBefore;
            if (entry == actor ? dispatches : gotCommand)
            {
                entry.NotifyChanged();
            }
        }

        // 充電ポートの予約を外したら、空いたことを知らせる（充電ポートが空くのを待っている AGV がいるかもしれない）
        foreach (var port in start.ReservedChargingPorts.Except(ReservedChargingPorts()))
        {
            var metadata = EventMetadata.Start($"charging-port:{port}", timeProvider);
            await publisher.PublishAsync(new ChargingPortReleased(metadata, port), cancellationToken).ConfigureAwait(false);
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
            if (entry.Control != ControlStatus.Manual)
            {
                entry.PendingEvents.Add(metadata => new AgvArrived(metadata, travel.Id, destination)); // マニュアルの行き先は知らせない
            }
        }

        if (outcome == NotificationOutcome.Accepted && IsManualStepping(entry))
        {
            _manualStepsChanged = true; // 1つずつの動作を終えた（パネルが空いたかもしれない）
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
        var specialPanels = SpecialPanelHeadings();
        var obstacles = new List<PlanningObstacle>();
        var participants = new List<AgvEntry>();
        foreach (var entry in _agvs.Values.Where(e => e.Travel.Position is not null && !IsDetachedForGood(e)))
        {
            entry.WasObstacle = false;
            if (!IsSystemControlled(entry) && !IsManualMoving(entry))
            {
                // マニュアル（行き先のないもの）、ロック、切り離しから戻る位置リセット待ちは、システムの判断では動かさない
                obstacles.Add(new PlanningObstacle(entry.Travel.Id.Value, OccupiedPanels(entry)));
            }
            else if (IsObstacle(entry))
            {
                entry.WasObstacle = entry.Travel.Status != AgvTravelStatus.ResettingPosition;
                obstacles.Add(new PlanningObstacle(entry.Travel.Id.Value, OccupiedPanels(entry)));
            }
            else
            {
                participants.Add(entry);
            }
        }

        var ordered = participants
            .OrderBy(e => !IsManualMoving(e))
            .ThenBy(e => e.Destination is null || e.Travel.Carrier != CarrierState.Raised)
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
                ? TravelSectionBuilder.Build(travel.PlanningStart, ToRouteSteps(travel.PlanningStart, plan.Actions.Skip(travel.CommittedCommands.Count)), specialPanels)
                : [];
            if (sections.Count > 0 || travel.RemainingSections.Count > 0)
            {
                travel.SetRoute(sections, dispatchNow: false);
            }
        }

        DispatchAll();
    }

    /// <summary>行き先と、まだ渡していない残りの経路を捨てる。</summary>
    private static void DropDestination(AgvEntry entry)
    {
        entry.Destination = null;
        entry.PlanStatus = null;
        if (entry.Travel.RemainingSections.Count > 0)
        {
            entry.Travel.SetRoute([], dispatchNow: false);
        }
    }

    /// <summary>
    /// 行き先で向く角度。マニュアルの行き先で着いたときの向きが指定されていれば、その向き。
    /// 特殊消耗品受け渡しパネルなら、キャリアを下ろす角度（着いたら回ってから止まる。マニュアルの行き先も同じ）。
    /// ほかは、向きを決めない。
    /// </summary>
    private int? GoalHeadingOf(AgvEntry entry, PanelId destination) =>
        entry.ArrivalHeading ?? (SpecialPanelHeadings().TryGetValue(destination.Value, out var heading) ? heading : null);

    /// <summary>特殊消耗品受け渡しパネルと、そのキャリアを下ろす角度。</summary>
    private Dictionary<string, int> SpecialPanelHeadings() =>
        CurrentMap().Panels
            .Where(panel => panel.Type == PanelType.SpecialTransfer && panel.CarrierLoweringHeading is not null)
            .ToDictionary(panel => panel.Id.Value, panel => panel.CarrierLoweringHeading!.Value.Degrees);

    /// <summary>特殊消耗品受け渡しパネル（走れない AGV が入れないノード）。</summary>
    private HashSet<string> SpecialTransferPanels() =>
        CurrentMap().Panels.Where(panel => panel.Type == PanelType.SpecialTransfer).Select(panel => panel.Id.Value).ToHashSet();

    /// <summary>予約されている充電ポート（充電ピンのあるパネル）。</summary>
    private HashSet<PanelId> ReservedChargingPorts() =>
        (mapStore.Current?.Panels ?? [])
            .Where(panel => panel.HasChargingPin && _reservations.OwnerOf(panel.Id) is not null)
            .Select(panel => panel.Id)
            .ToHashSet();

    /// <summary>障害物が塞ぐパネル：予約しているパネル（なければ、今いるパネル）。</summary>
    private string[] OccupiedPanels(AgvEntry entry) =>
        _reservations.ReservedBy(entry.Travel.Id).Select(p => p.Value).DefaultIfEmpty(entry.Travel.Position!.NodeId).ToArray();

    /// <summary>切り離し中で、戻す操作（位置リセット）もまだ受けていない。コマンドを渡さず、全台の計画にも入れない。</summary>
    private static bool IsDetachedForGood(AgvEntry entry) => entry.Control == ControlStatus.Detached && entry.PendingControl is null;

    /// <summary>
    /// マニュアルで並べた1つずつの動作を、渡そうとしたのに予約が取れなかった AGV は、そこで止めて残りを捨て、知らせる。
    /// </summary>
    private async Task DropBlockedManualStepsAsync(CancellationToken cancellationToken)
    {
        foreach (var entry in _agvs.Values)
        {
            var travel = entry.Travel;
            if (!IsManualStepping(entry) || travel.Status != AgvTravelStatus.Moving || travel.HasActiveSection || travel.RemainingSections.Count == 0)
            {
                continue;
            }

            travel.SetRoute([], dispatchNow: false);
            var metadata = EventMetadata.Start(entry.CorrelationId ?? $"agv:{travel.Id}", timeProvider);
            await publisher.PublishAsync(new ManualStepsDropped(metadata, travel.Id), cancellationToken).ConfigureAwait(false);
        }
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
                case FreeRotateCommand free:
                    committed.Add(new RotateAction(node, free.Heading));
                    break;
                case RotateSpecialCommand special:
                    committed.Add(new RotateAction(node, special.Heading));
                    break;
                case RotateCommand rotate:
                    committed.Add(new RotateAction(node, rotate.Heading));
                    break;
            }
        }

        var goal = entry.Destination is { } destination && travel.Carrier == CarrierState.Raised ? new AgentGoal(destination.Value, GoalHeadingOf(entry, destination)) : null;
        var motion = entry.Motion;
        return new PlanningAgent(
            travel.Id.Value,
            new AgentPose(pose.NodeId, pose.Heading),
            new AgentMotion(motion.ForwardSpeedMmPerSecond, motion.BackwardSpeedMmPerSecond, motion.RotationSpeedDegreesPerSecond),
            goal,
            committed,
            Movable: IsMovable(entry))
        {
            Due = goal is not null && entry.NeededBy is { } neededBy ? neededBy - timeProvider.GetUtcNow() : null,
            KeepOrder = IsManualMoving(entry), // マニュアルの行き先は、改善でも一番先のまま
            ForbiddenNodes = entry.CanTravelSpecialTransfer ? new HashSet<string>() : SpecialTransferPanels(),
        };
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
    /// 行き先がなく、通り道を塞いでいたら待機パネルへ動かしてよいか（動かせる状況か）。どかす動きを止めている間は動かさない。
    /// 待機中で、キャリアが上がっていて、充電ピンのあるパネル（充電中）にいないこと。
    /// </summary>
    private bool IsMovable(AgvEntry entry)
    {
        var travel = entry.Travel;
        return _evacuationEnabled
            && entry.Destination is null
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

    /// <summary>
    /// 緊急停止が長引いた（タイマーから）。障害物にして計画を立て直し、ほかの入り口と同じ後始末をする。
    /// ロックを外してから、組み立てがつないだ後始末（イベントを届ける）を呼ぶ。
    /// </summary>
    private async Task OnStoppedLongAsync(AgvEntry entry)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (entry.Travel.Status != AgvTravelStatus.EmergencyStopped || entry.StoppedLong)
            {
                return;
            }

            var start = BeginChange();
            entry.StoppedLong = true;
            ReplanAll();
            await CompleteChangeAsync(start, actor: null, dispatches: true, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        if (_afterBackgroundWork is { } afterBackgroundWork)
        {
            await afterBackgroundWork(CancellationToken.None).ConfigureAwait(false);
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
        var graph = RouteGraphFactory.FromMap(CurrentMap(), entry.CanTravelSpecialTransfer);
        return _planner.Plan(new RouteRequest(graph, entry.Travel.Position!, destination.Value, GoalHeadingOf(entry, destination), entry.Motion));
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

        /// <summary>コントロールステータス。</summary>
        public ControlStatus Control { get; set; }

        /// <summary>特殊消耗品受け渡しパネルを走れるか。</summary>
        public bool CanTravelSpecialTransfer { get; init; }

        /// <summary>最後に届いた充電残量（%）。起動してから一度も届いていなければ null（メモリにだけ持ち、再起動したら次に届くまでわからない）。</summary>
        public int? BatteryPercent { get; set; }

        /// <summary>切り離しから切り替える先。位置リセットの完了の通知を受けたら、これに切り替える。</summary>
        public ControlStatus? PendingControl { get; set; }

        /// <summary>今の一連の流れの ID（配車からの頼みに付いてきたもの）。</summary>
        public string? CorrelationId { get; set; }

        /// <summary>頼まれた行き先。着いたら null。設定し直すと、着いたときの向きも消す（向きは、行き先と一緒にだけ持つ）。</summary>
        public PanelId? Destination
        {
            get;
            set
            {
                field = value;
                ArrivalHeading = null;
            }
        }

        /// <summary>マニュアルの行き先で指定された、着いたときの向き。指定がなければ null。</summary>
        public int? ArrivalHeading { get; private set; }

        /// <summary>マニュアルの行き先を、着いたときの向きと一緒に設定する。</summary>
        public void SetManualDestination(PanelId destination, int? arrivalHeading)
        {
            Destination = destination;
            ArrivalHeading = arrivalHeading;
        }

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
