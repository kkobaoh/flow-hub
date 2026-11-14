using FlowHub.Map;
using FlowHub.Routing;

namespace FlowHub.Traffic.Domain;

/// <summary>
/// AGV の走行の状態。
/// </summary>
internal enum AgvTravelStatus
{
    /// <summary>位置がわからない（まだ位置リセットしていない）。</summary>
    Unregistered,

    /// <summary>待機中。走行の指示を持っていない。</summary>
    Idle,

    /// <summary>走行中。</summary>
    Moving,

    /// <summary>緊急停止中。アラームクリアで残りの指示の続きを走る。</summary>
    EmergencyStopped,

    /// <summary>完了の通知が指示と合わなかった。位置リセットが必要。</summary>
    Error,
}

/// <summary>位置リセットの結果。</summary>
internal sealed record PositionResetResult(ReservationResult Reservation, AgvInstruction? Instruction)
{
    public bool Succeeded => Reservation.Succeeded;
}

/// <summary>出発の結果。</summary>
internal sealed record StartRouteResult(ReservationResult Reservation, AgvInstruction? Instruction)
{
    public bool Succeeded => Reservation.Succeeded;
}

/// <summary>完了の通知を受けた結果。</summary>
/// <param name="Accepted">通知が指示どおりだったか。false のときは異常として止めた。</param>
/// <param name="Instruction">続けて AGV へ送るもの（次の区間など）。なければ null。</param>
internal sealed record NotificationResult(bool Accepted, AgvInstruction? Instruction);

/// <summary>
/// 1台の AGV の走行。位置の追跡、パネルの予約と解放、区間の指示の送り出しを受け持つ。
/// </summary>
internal sealed class AgvTravel(AgvId id, PanelReservations reservations)
{
    private readonly Queue<TravelSection> _remainingSections = new();
    private readonly Queue<TravelCommand> _pendingCommands = new();

    /// <summary>緊急停止中に区間を終えたため、アラームクリアのときに次の区間を送る必要がある。</summary>
    private bool _nextSectionDeferred;

    public AgvId Id { get; } = id;

    public AgvTravelStatus Status { get; private set; } = AgvTravelStatus.Unregistered;

    /// <summary>今の姿勢（最後に完了した動作のあとのパネルと向き）。位置がわからなければ null。</summary>
    public Pose? Position { get; private set; }

    /// <summary>送ったが、まだ完了の通知を受けていない指示。</summary>
    public IReadOnlyCollection<TravelCommand> PendingCommands => _pendingCommands;

    /// <summary>
    /// 位置を上書きする（最初の登録も同じ）。指定のパネルを予約し、ほかの予約と残りの指示を捨てる。
    /// 指定のパネルをほかの AGV が予約していたら、何も変えない。
    /// </summary>
    public PositionResetResult ResetPosition(Pose position)
    {
        ArgumentNullException.ThrowIfNull(position);

        var panel = new PanelId(position.NodeId);
        var reservation = reservations.TryReserve(Id, [panel]);
        if (!reservation.Succeeded)
        {
            return new PositionResetResult(reservation, null);
        }

        ReleaseAllExcept(panel);
        ClearRoute();
        Position = position;
        Status = AgvTravelStatus.Idle;

        return new PositionResetResult(reservation, new SendPositionReset(position));
    }

    /// <summary>
    /// 経路を走り始める。経路全体のパネルをまとめて予約できたときだけ、最初の区間を送る。
    /// </summary>
    public StartRouteResult StartRoute(IReadOnlyList<TravelSection> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        if (Status != AgvTravelStatus.Idle)
        {
            throw new InvalidOperationException($"{Id} は待機中でないため出発できません（今の状態：{Status}）。");
        }

        if (sections.Count == 0 || sections[0].Panels[0].Value != Position!.NodeId)
        {
            throw new ArgumentException($"経路が {Id} の今いるパネル（{Position!.NodeId}）から始まっていません。", nameof(sections));
        }

        var reservation = reservations.TryReserve(Id, sections.SelectMany(s => s.Panels));
        if (!reservation.Succeeded)
        {
            return new StartRouteResult(reservation, null);
        }

        foreach (var section in sections)
        {
            _remainingSections.Enqueue(section);
        }

        Status = AgvTravelStatus.Moving;
        return new StartRouteResult(reservation, DispatchNextSection());
    }

    /// <summary>
    /// 前進・後退の完了（十字の検出、または行き止まりのパネルへの到着）の通知。
    /// </summary>
    public NotificationResult OnMoveCompleted(PanelId arrived)
    {
        if (!IsAwaitingCompletion() || DestinationOf(_pendingCommands.Peek()) is not { } expected || arrived != expected)
        {
            return Fail();
        }

        _pendingCommands.Dequeue();
        var left = new PanelId(Position!.NodeId);
        Position = new Pose(arrived.Value, Position.Heading);

        if (left != arrived && !RemainingPanels().Contains(left))
        {
            reservations.Release(Id, left);
        }

        return new NotificationResult(true, AdvanceIfSectionDone());
    }

    /// <summary>
    /// 回転の完了の通知。回転後の絶対角度を受け取る。
    /// </summary>
    public NotificationResult OnRotationCompleted(int heading)
    {
        if (!IsAwaitingCompletion() || _pendingCommands.Peek() is not RotateCommand rotate || rotate.Heading != heading)
        {
            return Fail();
        }

        _pendingCommands.Dequeue();
        Position = new Pose(Position!.NodeId, heading);

        return new NotificationResult(true, AdvanceIfSectionDone());
    }

    /// <summary>
    /// 緊急停止する。予約はそのまま保つ（アラームクリアのあと、残りの指示の続きを走るため）。
    /// </summary>
    public AgvInstruction EmergencyStop()
    {
        if (Status == AgvTravelStatus.Unregistered)
        {
            throw new InvalidOperationException($"{Id} は位置がわからないため、緊急停止を送れません。");
        }

        // 異常のときは、異常のまま（位置リセットが必要）
        if (Status != AgvTravelStatus.Error)
        {
            Status = AgvTravelStatus.EmergencyStopped;
        }

        return new SendEmergencyStop();
    }

    /// <summary>
    /// アラームクリアする。残りの指示の続きを走る。緊急停止中に区間を終えていたら、次の区間もあわせて送る。
    /// </summary>
    public IReadOnlyList<AgvInstruction> ClearAlarm()
    {
        if (Status != AgvTravelStatus.EmergencyStopped)
        {
            throw new InvalidOperationException($"{Id} は緊急停止中でないため、アラームクリアできません（今の状態：{Status}）。");
        }

        var instructions = new List<AgvInstruction> { new SendAlarmClear() };
        var hasRoute = _pendingCommands.Count > 0 || _remainingSections.Count > 0;
        Status = hasRoute ? AgvTravelStatus.Moving : AgvTravelStatus.Idle;

        if (_nextSectionDeferred)
        {
            _nextSectionDeferred = false;
            instructions.Add(DispatchNextSection());
        }

        return instructions;
    }

    private bool IsAwaitingCompletion() =>
        Status is AgvTravelStatus.Moving or AgvTravelStatus.EmergencyStopped && _pendingCommands.Count > 0;

    private NotificationResult Fail()
    {
        Status = AgvTravelStatus.Error;
        return new NotificationResult(false, null);
    }

    /// <summary>
    /// 区間を終えていたら、次の区間を送るか、経路を走り終える。
    /// </summary>
    private SendTravelCommands? AdvanceIfSectionDone()
    {
        if (_pendingCommands.Count > 0)
        {
            return null;
        }

        if (_remainingSections.Count == 0)
        {
            Status = AgvTravelStatus.Idle;
            ReleaseAllExcept(new PanelId(Position!.NodeId));
            return null;
        }

        // 緊急停止中は送らず、アラームクリアのときに送る
        if (Status == AgvTravelStatus.EmergencyStopped)
        {
            _nextSectionDeferred = true;
            return null;
        }

        return DispatchNextSection();
    }

    private SendTravelCommands DispatchNextSection()
    {
        var section = _remainingSections.Dequeue();
        foreach (var command in section.Commands)
        {
            _pendingCommands.Enqueue(command);
        }

        return new SendTravelCommands(section.Commands);
    }

    /// <summary>
    /// これから通るパネル（送った指示の残りと、まだ送っていない区間）。
    /// </summary>
    private HashSet<PanelId> RemainingPanels()
    {
        var panels = new HashSet<PanelId>(_remainingSections.SelectMany(s => s.Panels));
        foreach (var destination in _pendingCommands.Select(DestinationOf).OfType<PanelId>())
        {
            panels.Add(destination);
        }

        return panels;
    }

    /// <summary>
    /// 前進・後退の移動先のパネル。回転なら null。
    /// </summary>
    private static PanelId? DestinationOf(TravelCommand command) => command switch
    {
        ForwardCommand forward => forward.To,
        BackwardCommand backward => backward.To,
        _ => null,
    };

    private void ReleaseAllExcept(PanelId keep)
    {
        foreach (var panel in reservations.ReservedBy(Id).Where(p => p != keep))
        {
            reservations.Release(Id, panel);
        }
    }

    private void ClearRoute()
    {
        _remainingSections.Clear();
        _pendingCommands.Clear();
        _nextSectionDeferred = false;
    }
}
