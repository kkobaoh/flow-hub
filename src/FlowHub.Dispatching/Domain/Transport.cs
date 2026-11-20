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

    /// <summary>割り当て前に取りやめた。</summary>
    Canceled,
}

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
        return MoveTo(TransportStage.MovingToSource).Actions;
    }

    /// <summary>AGV がパネルに着いた（交通整理から）。</summary>
    public TransportEventResult OnArrived(PanelId panel) => Stage switch
    {
        TransportStage.MovingToSource when panel == Request.Source.Panel => MoveTo(TransportStage.LoweringAtSource),
        TransportStage.MovingToDestination when panel == Request.Destination.Panel => MoveTo(TransportStage.LoweringAtDestination),
        _ => Ignored,
    };

    /// <summary>キャリアが下がった（交通整理から）。</summary>
    public TransportEventResult OnCarrierLowered() => Stage switch
    {
        TransportStage.LoweringAtSource => MoveTo(TransportStage.TransferringAtSource),
        TransportStage.LoweringAtDestination => MoveTo(TransportStage.TransferringAtDestination),
        _ => Ignored,
    };

    /// <summary>受け渡しの相手が、受け渡しを終えた（消耗品の管理から）。</summary>
    public TransportEventResult OnTransferCompleted() => Stage switch
    {
        TransportStage.TransferringAtSource => MoveTo(TransportStage.RaisingAtSource),
        TransportStage.TransferringAtDestination => MoveTo(TransportStage.RaisingAtDestination),
        _ => Ignored,
    };

    /// <summary>キャリアが上がった（交通整理から）。</summary>
    public TransportEventResult OnCarrierRaised() => Stage switch
    {
        TransportStage.RaisingAtSource => MoveTo(TransportStage.MovingToDestination),
        TransportStage.RaisingAtDestination => MoveTo(TransportStage.Completed, new NotifyCompleted(Id)),
        _ => Ignored,
    };

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
            if (_pauses.Count > 0)
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
