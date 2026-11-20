using FlowHub.Map;
using FlowHub.Traffic;

namespace FlowHub.Dispatching.Domain;

/// <summary>
/// 搬送の段階。届けるときも回収するときも同じ流れ。
/// </summary>
internal enum TransportStage
{
    Waiting,
    MovingToSource,
    LoweringAtSource,
    TransferringAtSource,
    RaisingAtSource,
    MovingToDestination,
    LoweringAtDestination,
    TransferringAtDestination,
    RaisingAtDestination,
    Completed,

    /// <summary>AGV が異常になったため中断した。</summary>
    Interrupted,

    /// <summary>取りやめた（割り当て前、または割り当てたあと）。</summary>
    Canceled,
}

/// <summary>取りやめを頼んだ結果と、続けて頼むことや知らせること。</summary>
internal sealed record TransportCancelOutcome(TransportCancelResult Result, IReadOnlyList<TransportAction> Actions);

/// <summary>
/// 知らせを受けた結果。
/// </summary>
/// <param name="Accepted">段階と合う知らせだったか。false のときは状態を変えていない（ログに残して画面で警告する）。</param>
/// <param name="Actions">続けて頼むことや知らせること。</param>
internal sealed record TransportEventResult(bool Accepted, IReadOnlyList<TransportAction> Actions);

/// <summary>
/// 搬送を一時停止する理由。理由が1つでも残っていれば、次のコマンドを出さない。
/// </summary>
internal enum PauseReason
{
    /// <summary>AGV が緊急停止中。アラームクリアの完了で外れる。</summary>
    AgvStopped,

    /// <summary>システムの運転状態が停止。スタートで外れる。</summary>
    OperationStopped,
}

/// <summary>
/// 1回の搬送（1往復）。搬送の流れを1か所で進める状態遷移。
/// </summary>
internal sealed class Transport(TransportId id, TransportRequest request)
{
    private static readonly TransportEventResult Ignored = new(false, []);

    private readonly HashSet<PauseReason> _pauses = [];

    /// <summary>一時停止中に、今の段階のコマンドを出さずにおいたか。</summary>
    private bool _commandDeferred;

    /// <summary>割り当てたあとに取りやめを頼まれた（受け渡しの途中なので、キャリアを上げたところで止まる）。</summary>
    private bool _cancelRequested;

    /// <summary>搬送元で受け渡しを終えた（積み込んだ）。</summary>
    private bool _loaded;

    /// <summary>搬送先で受け渡しを終えた（降ろした）。</summary>
    private bool _unloaded;

    public TransportId Id { get; } = id;

    public TransportRequest Request { get; } = request;

    public TransportStage Stage { get; private set; } = TransportStage.Waiting;

    public AgvId? AssignedAgv { get; private set; }

    /// <summary>中断したときの段階。中断していなければ null。</summary>
    public TransportStage? InterruptedAt { get; private set; }

    /// <summary>
    /// AGV を割り当てる。AGV を搬送元へ動かす。
    /// </summary>
    public IReadOnlyList<TransportAction> Assign(AgvId agv)
    {
        if (Stage != TransportStage.Waiting)
        {
            throw new InvalidOperationException($"搬送 {Id} は割り当て待ちでないため、割り当てられません（今の段階：{Stage}）。");
        }

        AssignedAgv = agv;
        if (Request.FromAgv is not null)
        {
            _loaded = true; // AGV 自身が搬送元：積み込みを飛ばして、搬送先へ向かう
            return MoveTo(TransportStage.MovingToDestination).Actions;
        }

        return MoveTo(TransportStage.MovingToSource).Actions;
    }

    /// <summary>AGV がパネルに着いた（交通整理から）。</summary>
    public TransportEventResult OnArrived(PanelId panel) => Stage switch
    {
        TransportStage.MovingToSource when panel == Request.Source.Panel => MoveTo(TransportStage.LoweringAtSource),
        TransportStage.MovingToDestination when panel == Request.Destination.Panel => MoveTo(TransportStage.LoweringAtDestination),
        _ => Ignored,
    };

    /// <summary>キャリアが下がった（交通整理から）。取りやめを頼まれていれば、受け渡しをせずにキャリアを上げる。</summary>
    public TransportEventResult OnCarrierLowered() => Stage switch
    {
        TransportStage.LoweringAtSource => MoveTo(_cancelRequested ? TransportStage.RaisingAtSource : TransportStage.TransferringAtSource),
        TransportStage.LoweringAtDestination => MoveTo(_cancelRequested ? TransportStage.RaisingAtDestination : TransportStage.TransferringAtDestination),
        _ => Ignored,
    };

    /// <summary>受け渡しの相手が、受け渡しを終えた（消耗品の管理から）。</summary>
    public TransportEventResult OnTransferCompleted()
    {
        switch (Stage)
        {
            case TransportStage.TransferringAtSource:
                _loaded = true;
                return MoveTo(TransportStage.RaisingAtSource);
            case TransportStage.TransferringAtDestination:
                _unloaded = true;
                return MoveTo(TransportStage.RaisingAtDestination);
            default:
                return Ignored;
        }
    }

    /// <summary>キャリアが上がった（交通整理から）。取りやめを頼まれていれば、そこで止まって取りやめる（搬送先で降ろしたあとなら完了）。</summary>
    public TransportEventResult OnCarrierRaised() => Stage switch
    {
        TransportStage.RaisingAtSource when _cancelRequested => MoveTo(TransportStage.Canceled, new NotifyCanceled(Id, AssignedAgv!.Value, _loaded)),
        TransportStage.RaisingAtSource => MoveTo(TransportStage.MovingToDestination),
        TransportStage.RaisingAtDestination when _cancelRequested && !_unloaded => MoveTo(TransportStage.Canceled, new NotifyCanceled(Id, AssignedAgv!.Value, Loaded: true)),
        TransportStage.RaisingAtDestination => MoveTo(TransportStage.Completed, new NotifyCompleted(Id)),
        _ => Ignored,
    };

    /// <summary>
    /// 割り当てたあとの搬送の取りやめを頼まれた（AGV へのコマンドの単位で取りやめる）。
    /// 走っていれば AGV を止めてすぐに取りやめる。受け渡しの途中なら、許可の前ならキャリアを上げて、許可のあとなら受け渡しを終えて
    /// キャリアを上げたところで取りやめる。搬送先で許可を出したあとなら、最後まで終えて完了にする。
    /// 運転状態が停止でも、取りやめに要るコマンドは出す（緊急停止中は、アラームクリアのあとに出す）。
    /// </summary>
    public TransportCancelOutcome RequestCancel()
    {
        var agv = AssignedAgv;
        switch (Stage)
        {
            case TransportStage.MovingToSource or TransportStage.MovingToDestination:
                var loaded = Stage == TransportStage.MovingToDestination;
                Stage = TransportStage.Canceled;
                _commandDeferred = false;
                return new(TransportCancelResult.Canceled, [new StopAgv(agv!.Value), new NotifyCanceled(Id, agv.Value, loaded)]);

            case TransportStage.LoweringAtSource or TransportStage.LoweringAtDestination when _commandDeferred:
                // キャリアを下げるコマンドをまだ出していない：下げずに、そのまま取りやめる
                var loadedHere = Stage == TransportStage.LoweringAtDestination;
                Stage = TransportStage.Canceled;
                _commandDeferred = false;
                return new(TransportCancelResult.Canceled, [new NotifyCanceled(Id, agv!.Value, loadedHere)]);

            case TransportStage.LoweringAtSource or TransportStage.LoweringAtDestination:
                _cancelRequested = true;
                return new(TransportCancelResult.Canceling, []);

            case TransportStage.TransferringAtSource or TransportStage.TransferringAtDestination when _commandDeferred:
                // 受け渡しの許可をまだ頼んでいない（許可の前）：受け渡しをせずに、キャリアを上げる
                _cancelRequested = true;
                _commandDeferred = false;
                return new(TransportCancelResult.Canceling, MoveTo(Stage == TransportStage.TransferringAtSource ? TransportStage.RaisingAtSource : TransportStage.RaisingAtDestination).Actions);

            case TransportStage.TransferringAtSource or TransportStage.RaisingAtSource:
                _cancelRequested = true;
                return new(TransportCancelResult.Canceling, ReleaseDeferredForCancel());

            case TransportStage.TransferringAtDestination or TransportStage.RaisingAtDestination:
                _cancelRequested = true;
                return new(TransportCancelResult.Completing, ReleaseDeferredForCancel());

            default:
                return new(TransportCancelResult.NotCancelable, []);
        }
    }

    /// <summary>運転状態の停止で出さずにおいたコマンドがあれば、取りやめのために今出す（緊急停止中は出さない）。</summary>
    private IReadOnlyList<TransportAction> ReleaseDeferredForCancel()
    {
        if (!_commandDeferred || _pauses.Contains(PauseReason.AgvStopped) || CommandFor(Stage) is not { } command)
        {
            return [];
        }

        _commandDeferred = false;
        return [command];
    }

    /// <summary>
    /// 一時停止する（AGV が緊急停止中になった、運転状態が停止になった）。一時停止中は、状態は進めるが、次のコマンドは出さない。
    /// </summary>
    public void Pause(PauseReason reason) => _pauses.Add(reason);

    /// <summary>
    /// 一時停止の理由を外す（アラームクリアが完了した、運転状態がスタートになった）。
    /// ほかの理由が残っていなければ、一時停止中に出さなかったコマンドを出す。
    /// </summary>
    public IReadOnlyList<TransportAction> Resume(PauseReason reason)
    {
        _pauses.Remove(reason);
        if (_pauses.Count > 0 || !_commandDeferred)
        {
            return [];
        }

        _commandDeferred = false;
        return CommandFor(Stage) is { } command ? [command] : [];
    }

    /// <summary>一時停止中か（理由が1つでも残っている）。</summary>
    public bool IsPaused => _pauses.Count > 0;

    /// <summary>
    /// 移動の段階なら、その移動（運転状態を停止にして待機パネルへ動かしたあと、もう一度向かわせるのに使う）。移動の段階でなければ null。
    /// </summary>
    public MoveAgv? CurrentMove() =>
        Stage is TransportStage.MovingToSource or TransportStage.MovingToDestination ? (MoveAgv)CommandFor(Stage)! : null;

    /// <summary>
    /// AGV が異常になった。搬送を中断して知らせる。
    /// </summary>
    public IReadOnlyList<TransportAction> Interrupt()
    {
        InterruptedAt = Stage;
        Stage = TransportStage.Interrupted;
        return [new NotifyInterrupted(Id)];
    }

    /// <summary>
    /// 割り当て前の搬送を取りやめる。
    /// </summary>
    public void Cancel()
    {
        if (Stage != TransportStage.Waiting)
        {
            throw new InvalidOperationException($"搬送 {Id} は割り当て前でないため、取りやめられません（今の段階：{Stage}）。");
        }

        Stage = TransportStage.Canceled;
    }

    /// <summary>
    /// 次の段階に進む。起きた事実の知らせはすぐに出し、次の段階のコマンドは一時停止中なら出さずにおく。
    /// </summary>
    private TransportEventResult MoveTo(TransportStage next, params TransportAction[] facts)
    {
        Stage = next;
        var actions = new List<TransportAction>(facts);

        if (CommandFor(next) is { } command)
        {
            // 取りやめの途中は、運転状態の停止では止めない（緊急停止中だけ待つ）
            if (_cancelRequested ? _pauses.Contains(PauseReason.AgvStopped) : _pauses.Count > 0)
            {
                _commandDeferred = true;
            }
            else
            {
                actions.Add(command);
            }
        }

        return new TransportEventResult(true, actions);
    }

    /// <summary>
    /// その段階で出すコマンド。
    /// </summary>
    private TransportAction? CommandFor(TransportStage stage) => stage switch
    {
        TransportStage.MovingToSource => new MoveAgv(AssignedAgv!.Value, Request.Source.Panel),
        TransportStage.LoweringAtSource or TransportStage.LoweringAtDestination => new LowerCarrier(AssignedAgv!.Value),
        TransportStage.TransferringAtSource => new RequestTransferPermission(Id, Request.Source, TransferSide.Load),
        TransportStage.TransferringAtDestination => new RequestTransferPermission(Id, Request.Destination, TransferSide.Unload),
        TransportStage.RaisingAtSource or TransportStage.RaisingAtDestination => new RaiseCarrier(AssignedAgv!.Value),
        TransportStage.MovingToDestination => new MoveAgv(AssignedAgv!.Value, Request.Destination.Panel),
        _ => null,
    };
}
