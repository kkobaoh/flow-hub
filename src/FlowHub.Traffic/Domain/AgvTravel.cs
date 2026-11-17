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

    /// <summary>位置リセットの完了の通知を待っている。</summary>
    ResettingPosition,

    /// <summary>待機中。走る経路を持っていない。</summary>
    Idle,

    /// <summary>走行中。</summary>
    Moving,

    /// <summary>緊急停止中（緊急停止、または位置はずれていない種類のエラー）。アラームクリアで続きを走れる。</summary>
    EmergencyStopped,

    /// <summary>異常（通知の食い違い、位置がずれた可能性のあるエラー、AGV の再起動）。位置リセットが必要。予約は保つ。</summary>
    Error,
}

/// <summary>位置リセットの結果。</summary>
internal sealed record PositionResetResult(ReservationResult Reservation)
{
    public bool Succeeded => Reservation.Succeeded;
}

/// <summary>出発の結果。</summary>
internal sealed record StartRouteResult(ReservationResult Reservation)
{
    public bool Succeeded => Reservation.Succeeded;
}

/// <summary>
/// 1台の AGV の走行。コマンドの番号付けと受け渡し、位置の追跡、パネルの予約と解放、通知の照合を受け持つ。
/// </summary>
/// <remarks>
/// コマンドは、AGV が GET で取りに来たときに <see cref="OnCommandRequest"/> で渡す。
/// GET に載っている「前回受信したコマンドの番号」で、AGV がどこまで受け取ったかを判断する。
/// </remarks>
internal sealed class AgvTravel(AgvId id, PanelReservations reservations)
{
    private const long NoneReceived = -1;

    private readonly List<Outgoing> _outbox = [];
    private readonly List<TravelSection> _remainingSections = [];
    private readonly HashSet<long> _motionNumbers = [];

    private long _nextNumber = 1;
    private ActiveMotion? _active;
    private long? _awaitingAlarmClear;
    private long? _awaitingPositionReset;

    /// <summary>これより小さい番号の動作コマンドは、位置リセットで捨てたもの。</summary>
    private long _staleBelow;

    /// <summary>直前に受け付けた完了の通知（重複の見分けに使う）。</summary>
    private StepCompletion? _lastAccepted;

    private bool _error;
    private bool _stopped;

    public AgvId Id { get; } = id;

    /// <summary>今の姿勢（最後に完了した動作のあとのパネルと向き）。位置がわからなければ null。</summary>
    public Pose? Position { get; private set; }

    public AgvTravelStatus Status =>
        Position is null ? AgvTravelStatus.Unregistered
        : _error ? AgvTravelStatus.Error
        : _stopped ? AgvTravelStatus.EmergencyStopped
        : _awaitingPositionReset is not null ? AgvTravelStatus.ResettingPosition
        : _active is not null || _remainingSections.Count > 0 ? AgvTravelStatus.Moving
        : AgvTravelStatus.Idle;

    // ---- システムからの操作 ----

    /// <summary>
    /// 位置を上書きする（最初の登録も同じ）。指定のパネルを予約し、ほかの予約と残りの経路を捨て、
    /// まだ渡していない動作コマンドを取り下げる。完了の通知を受けるまで、出発できない。
    /// 指定のパネルをほかの AGV が予約していたら、何も変えない。
    /// </summary>
    public PositionResetResult ResetPosition(Pose position)
    {
        ArgumentNullException.ThrowIfNull(position);

        var panel = new PanelId(position.NodeId);
        var reservation = reservations.TryReserve(Id, [panel]);
        if (!reservation.Succeeded)
        {
            return new PositionResetResult(reservation);
        }

        ReleaseAllExcept(panel);
        WithdrawMotions(returnSections: false);
        _remainingSections.Clear();
        _active = null;
        _awaitingAlarmClear = null;
        _lastAccepted = null;
        _error = false;
        _stopped = false;
        Position = position;

        var command = Enqueue(number => new PositionResetCommand(number, position));
        _awaitingPositionReset = command.Number;
        _staleBelow = command.Number; // これより前の動作コマンドは、AGV が破棄する

        return new PositionResetResult(reservation);
    }

    /// <summary>
    /// 経路を走り始める。経路全体のパネルをまとめて予約できたときだけ、最初の区間の動作コマンドを用意する。
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
            return new StartRouteResult(reservation);
        }

        _remainingSections.AddRange(sections);
        DispatchNextSection();
        return new StartRouteResult(reservation);
    }

    /// <summary>
    /// 緊急停止する。予約は保ち、まだ渡していない動作コマンドを取り下げる（アラームクリアのあとに渡し直す）。
    /// </summary>
    public void EmergencyStop()
    {
        if (Status == AgvTravelStatus.Unregistered)
        {
            throw new InvalidOperationException($"{Id} は位置がわからないため、緊急停止を送れません。");
        }

        WithdrawMotions(returnSections: true);
        if (!_error)
        {
            _stopped = true; // 異常のときは、異常のまま
        }

        Enqueue(number => new EmergencyStopCommand(number));
    }

    /// <summary>
    /// アラームクリアする。完了の通知を受けるまでは、緊急停止中のまま。
    /// </summary>
    public void ClearAlarm()
    {
        if (Status != AgvTravelStatus.EmergencyStopped)
        {
            throw new InvalidOperationException($"{Id} は緊急停止中でないため、アラームクリアできません（今の状態：{Status}）。");
        }

        _awaitingAlarmClear = Enqueue(number => new AlarmClearCommand(number)).Number;
    }

    // ---- AGV からのリクエストと通知 ----

    /// <summary>
    /// AGV がコマンドを取りに来た（GET）。渡すコマンドを返す。なければ空。
    /// </summary>
    /// <param name="lastReceived">AGV が前回受信したコマンドの番号。再起動や初めて動かすときは -1。</param>
    public IReadOnlyList<AgvCommand> OnCommandRequest(long lastReceived)
    {
        if (lastReceived == NoneReceived)
        {
            return OnRequestWithoutHistory();
        }

        foreach (var item in _outbox.ToArray())
        {
            if (item.Command.Number <= lastReceived)
            {
                _outbox.Remove(item); // 受け取った
            }
            else if (item.WithdrawIfNotReceived)
            {
                _outbox.Remove(item); // 受け取っていなかったので、取り下げる
                DropActiveMotion(item, item.ReturnSectionOnWithdraw);
            }
        }

        return HandOut(_outbox);
    }

    /// <summary>
    /// 再起動した AGV（または初めて動かす AGV）からの GET。走りかけの動作コマンドは渡し直さず、位置リセットだけを渡す。
    /// </summary>
    private List<AgvCommand> OnRequestWithoutHistory()
    {
        // 位置リセットの前に再起動した：残りの指示を忘れたので、予約を保ったまま位置リセットを待つ
        if (Position is not null && _awaitingPositionReset is null)
        {
            _error = true;
        }

        _outbox.RemoveAll(item => item.Command is not PositionResetCommand);
        return HandOut(_outbox);
    }

    /// <summary>
    /// 再起動した AGV からの、位置を取得するリクエスト。最後に把握していた位置をそのまま返す。
    /// </summary>
    public Pose? OnPositionRequest() => Position;

    /// <summary>
    /// 動作コマンドの受信の完了の通知。ログに残すだけ（受信は GET の番号で判断する）。
    /// </summary>
    public NotificationOutcome OnMotionReceived(long number)
    {
        LastReceiptAcknowledged = number; // 状態は変えず、画面などで見られるように覚えておくだけ
        return NotificationOutcome.Logged;
    }

    /// <summary>
    /// 最後に受信の完了の通知が届いたコマンドの番号（ログや画面のため。受信の判断には使わない）。まだなければ null。
    /// </summary>
    public long? LastReceiptAcknowledged { get; private set; }

    /// <summary>
    /// 前進・後退の完了の通知（十字の検出、または行き止まりのパネルへの到着）。
    /// </summary>
    public NotificationOutcome OnMoveCompleted(long number, MoveDirection direction, PanelId arrived) =>
        OnStepCompleted(new StepCompletion(number, direction, arrived));

    /// <summary>
    /// 回転の完了の通知。角度の誤差は AGV の制御の責務なので、角度は照合せず、向きは指示した角度にする。
    /// </summary>
    public NotificationOutcome OnRotationCompleted(long number, int reportedHeading)
    {
        _ = reportedHeading; // 照合しない
        return OnStepCompleted(new StepCompletion(number, Direction: null, Arrived: null));
    }

    /// <summary>
    /// アラームクリアの完了の通知。緊急停止中に用意できなかった次の区間があれば、ここで用意する。
    /// </summary>
    public NotificationOutcome OnAlarmCleared(long number)
    {
        if (_awaitingAlarmClear != number)
        {
            return NotificationOutcome.IgnoredUnexpected;
        }

        _awaitingAlarmClear = null;
        _stopped = false;
        if (_active is null && _remainingSections.Count > 0 && !_error)
        {
            DispatchNextSection();
        }

        return NotificationOutcome.Accepted;
    }

    /// <summary>
    /// 位置リセットの完了の通知。
    /// </summary>
    public NotificationOutcome OnPositionResetCompleted(long number)
    {
        if (_awaitingPositionReset != number)
        {
            return NotificationOutcome.IgnoredUnexpected;
        }

        _awaitingPositionReset = null;
        return NotificationOutcome.Accepted;
    }

    /// <summary>
    /// AGV からのエラーの通知。エラーを出した AGV は止まっている。
    /// 位置がずれた可能性があれば「異常」、なければ「緊急停止中」にし、まだ渡していない動作コマンドを取り下げる。
    /// </summary>
    public NotificationOutcome OnAgvError(AgvErrorKind kind)
    {
        if (Position is null)
        {
            return NotificationOutcome.Logged;
        }

        if (kind.MayShiftPosition())
        {
            WithdrawMotions(returnSections: false);
            _error = true;
        }
        else
        {
            WithdrawMotions(returnSections: true);
            if (!_error)
            {
                _stopped = true;
            }
        }

        return NotificationOutcome.Accepted;
    }

    // ---- 完了の通知の照合 ----

    private NotificationOutcome OnStepCompleted(StepCompletion completion)
    {
        var number = completion.Number;
        if (number <= 0 || number >= _nextNumber || !_motionNumbers.Contains(number))
        {
            return NotificationOutcome.Rejected; // 送っていない番号、または動作コマンドでない番号
        }

        if (number < _staleBelow)
        {
            return NotificationOutcome.IgnoredStale;
        }

        if (completion == _lastAccepted || _active is null || number != _active.Number)
        {
            return NotificationOutcome.IgnoredDuplicate; // 直前と同じ通知、または走り終えた動作コマンドへの通知
        }

        var remaining = _active.Remaining;
        if (completion.Matches(remaining[0]))
        {
            Accept(completion, skip: 0);
            return NotificationOutcome.Accepted;
        }

        if (_error)
        {
            // 異常のときは、先の動作と中身が合えば、手前の動作も完了したものとみなして受け付ける
            var index = remaining.FindIndex(completion.Matches);
            if (index < 0)
            {
                return NotificationOutcome.IgnoredUnexpected;
            }

            Accept(completion, skip: index);
            return NotificationOutcome.Accepted;
        }

        EnterFault();
        return NotificationOutcome.Fault;
    }

    private void Accept(StepCompletion completion, int skip)
    {
        var remaining = _active!.Remaining;
        remaining.RemoveRange(0, skip);
        var step = remaining[0];
        remaining.RemoveAt(0);
        _lastAccepted = completion;

        var left = new PanelId(Position!.NodeId);
        Position = step switch
        {
            RotateCommand rotate => new Pose(Position.NodeId, rotate.Heading),
            _ => new Pose(completion.Arrived!.Value.Value, Position.Heading),
        };

        // 異常のときは予約を保つ
        var current = new PanelId(Position.NodeId);
        if (!_error && left != current && !RemainingPanels().Contains(left))
        {
            reservations.Release(Id, left);
        }

        if (remaining.Count == 0)
        {
            FinishSection();
        }
    }

    /// <summary>
    /// 区間の最後の動作を終えた。次の区間を用意するか、経路を走り終える。止まっているときは、次の区間を用意しない。
    /// </summary>
    private void FinishSection()
    {
        _active = null;
        if (_remainingSections.Count > 0)
        {
            if (!_stopped && !_error)
            {
                DispatchNextSection();
            }

            return;
        }

        if (!_error)
        {
            ReleaseAllExcept(new PanelId(Position!.NodeId));
        }
    }

    /// <summary>
    /// 食い違う通知を受けた。緊急停止を用意して「異常」にする。予約は保つ。
    /// </summary>
    private void EnterFault()
    {
        _error = true;
        WithdrawMotions(returnSections: false);
        Enqueue(number => new EmergencyStopCommand(number));
    }

    // ---- コマンドの用意と受け渡し ----

    private void DispatchNextSection()
    {
        var section = _remainingSections[0];
        _remainingSections.RemoveAt(0);

        var command = (MotionCommand)Enqueue(number => new MotionCommand(number, section.Commands), section);
        _motionNumbers.Add(command.Number);
        _active = new ActiveMotion(command.Number, section, [.. section.Commands]);
    }

    private AgvCommand Enqueue(Func<long, AgvCommand> create, TravelSection? section = null)
    {
        var command = create(_nextNumber++);
        _outbox.Add(new Outgoing(command, section));
        return command;
    }

    private static List<AgvCommand> HandOut(List<Outgoing> items)
    {
        foreach (var item in items)
        {
            item.HandedOut = true;
        }

        return items.Select(item => item.Command).ToList();
    }

    /// <summary>
    /// まだ AGV に渡していない動作コマンドを取り下げる。渡したばかりで受け取ったかわからないものは、
    /// 次の GET で受け取っていなかったとわかったときに取り下げる。
    /// </summary>
    /// <param name="returnSections">取り下げた区間を、あとで渡し直すために経路に戻すか。</param>
    private void WithdrawMotions(bool returnSections)
    {
        foreach (var item in _outbox.Where(item => item.Command is MotionCommand).ToArray())
        {
            if (item.HandedOut)
            {
                item.WithdrawIfNotReceived = true;
                item.ReturnSectionOnWithdraw = returnSections;
            }
            else
            {
                _outbox.Remove(item);
                DropActiveMotion(item, returnSections);
            }
        }
    }

    private void DropActiveMotion(Outgoing item, bool returnSection)
    {
        if (_active is null || _active.Number != item.Command.Number)
        {
            return;
        }

        if (returnSection)
        {
            _remainingSections.Insert(0, _active.Section);
        }

        _active = null;
    }

    /// <summary>
    /// これから通るパネル（走っている動作コマンドの残りと、まだ用意していない区間）。
    /// </summary>
    private HashSet<PanelId> RemainingPanels()
    {
        var panels = new HashSet<PanelId>(_remainingSections.SelectMany(s => s.Panels));
        foreach (var step in _active?.Remaining ?? [])
        {
            if (step is ForwardCommand forward)
            {
                panels.Add(forward.To);
            }
            else if (step is BackwardCommand backward)
            {
                panels.Add(backward.To);
            }
        }

        return panels;
    }

    private void ReleaseAllExcept(PanelId keep)
    {
        foreach (var panel in reservations.ReservedBy(Id).Where(p => p != keep))
        {
            reservations.Release(Id, panel);
        }
    }

    /// <summary>
    /// 渡すために用意したコマンド。
    /// </summary>
    private sealed class Outgoing(AgvCommand command, TravelSection? section)
    {
        public AgvCommand Command { get; } = command;

        /// <summary>動作コマンドなら、その区間。</summary>
        public TravelSection? Section { get; } = section;

        /// <summary>レスポンスで AGV に渡したか（受け取ったかどうかは、次の GET でわかる）。</summary>
        public bool HandedOut { get; set; }

        /// <summary>次の GET で受け取っていなかったとわかったら取り下げるか。</summary>
        public bool WithdrawIfNotReceived { get; set; }

        /// <summary>取り下げたとき、区間を経路に戻すか。</summary>
        public bool ReturnSectionOnWithdraw { get; set; }
    }

    /// <summary>
    /// AGV が走っている動作コマンドと、まだ完了の通知を受けていない動作。
    /// </summary>
    private sealed record ActiveMotion(long Number, TravelSection Section, List<TravelCommand> Remaining);

    /// <summary>
    /// 1つの動作の完了の通知。前進・後退なら向きと着いたパネル、回転なら両方 null。
    /// </summary>
    private sealed record StepCompletion(long Number, MoveDirection? Direction, PanelId? Arrived)
    {
        public bool Matches(TravelCommand step) => step switch
        {
            ForwardCommand forward => Direction == MoveDirection.Forward && Arrived == forward.To,
            BackwardCommand backward => Direction == MoveDirection.Backward && Arrived == backward.To,
            RotateCommand => Direction is null,
            _ => false,
        };
    }
}
