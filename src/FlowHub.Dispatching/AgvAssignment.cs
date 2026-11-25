using FlowHub.Map;
using FlowHub.Traffic;

namespace FlowHub.Dispatching;

/// <summary>
/// 割り当て済みの搬送の段階（画面に出すため）。
/// </summary>
public enum AssignedTransportStage
{
    MovingToSource,
    LoweringAtSource,
    TransferringAtSource,
    RaisingAtSource,
    MovingToDestination,
    LoweringAtDestination,
    TransferringAtDestination,
    RaisingAtDestination,

    /// <summary>AGV が異常になったため中断した（位置リセットのあとに、やり直すか取りやめるかを選ぶ）。</summary>
    Interrupted,
}

/// <summary>搬送元。受け渡しをする場所か、ものを積んだまま止まっている AGV 自身。</summary>
public abstract record AssignmentSource;

/// <summary>搬送元が、受け渡しをする場所。</summary>
/// <param name="Panel">受け渡しパネル。</param>
/// <param name="Endpoint">受け渡しの相手（装置やステーション）の参照。配車にとっては中身のわからない値。</param>
public sealed record SourceTransferPoint(PanelId Panel, string Endpoint) : AssignmentSource;

/// <summary>搬送元が、ものを積んだまま止まっている AGV 自身。</summary>
public sealed record SourceAgv(AgvId Agv) : AssignmentSource;

/// <summary>
/// AGV に割り当てている搬送（画面に出すため）。
/// </summary>
/// <param name="DestinationEndpoint">搬送先の受け渡しの相手の参照。配車にとっては中身のわからない値。</param>
/// <param name="Paused">一時停止しているか（運転状態の停止、AGV の緊急停止など）。</param>
public sealed record AgvAssignment(
    AgvId Agv,
    TransportId Transport,
    AssignmentSource Source,
    PanelId DestinationPanel,
    string DestinationEndpoint,
    AssignedTransportStage Stage,
    bool Paused);

/// <summary>
/// 搬送の一覧の1件（画面に出すため）。待ち行列にあるものと、AGV に割り当てているもの（中断したものも含む）。
/// </summary>
/// <param name="Requester">頼んだ側。</param>
/// <param name="CargoRefs">運ぶものの参照。配車にとっては中身のわからない値。</param>
/// <param name="DestinationEndpoint">搬送先の受け渡しの相手の参照。</param>
/// <param name="NeededBy">必要時刻。</param>
/// <param name="Progress">割り当て待ちか、割り当てた AGV と段階か。</param>
public sealed record TransportListing(
    TransportId Transport,
    string Requester,
    IReadOnlyList<string> CargoRefs,
    AssignmentSource Source,
    PanelId DestinationPanel,
    string DestinationEndpoint,
    DateTimeOffset NeededBy,
    TransportProgress Progress)
{
    public bool Equals(TransportListing? other) =>
        other is not null
        && Transport == other.Transport
        && Requester == other.Requester
        && CargoRefs.SequenceEqual(other.CargoRefs)
        && Source == other.Source
        && DestinationPanel == other.DestinationPanel
        && DestinationEndpoint == other.DestinationEndpoint
        && NeededBy == other.NeededBy
        && Progress == other.Progress;

    public override int GetHashCode() => HashCode.Combine(Transport, NeededBy, Progress);
}

/// <summary>搬送の進み具合。</summary>
public abstract record TransportProgress;

/// <summary>待ち行列で、AGV の割り当てを待っている。</summary>
public sealed record WaitingForAgv : TransportProgress;

/// <summary>AGV に割り当てている。</summary>
/// <param name="Paused">一時停止しているか（運転状態の停止、AGV の緊急停止など）。</param>
public sealed record AssignedTo(AgvId Agv, AssignedTransportStage Stage, bool Paused) : TransportProgress;
