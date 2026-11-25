using FlowHub.Dispatching;
using FlowHub.Traffic;

namespace FlowHub.Api.Composition.Screens;

/// <summary>
/// 画面へ送る、すべての AGV の様子。変わるたびに番号を進める。
/// </summary>
/// <param name="Version">様子の番号。1から始まり、変わるたびに1つ進む。</param>
/// <param name="Agvs">AGV ごとの様子（ID の順）。</param>
internal sealed record AgvsSnapshot(long Version, IReadOnlyList<AgvSnapshot> Agvs);

/// <summary>
/// 画面へ送る、割り当て済みの搬送。
/// </summary>
/// <param name="Id">搬送の番号。</param>
/// <param name="From">搬送元の名前（受け渡しの相手。ものを積んだまま止まっていた AGV 自身なら、その AGV）。</param>
/// <param name="To">搬送先の名前（受け渡しの相手）。</param>
/// <param name="Stage">段階。<c>movingToSource</c>、<c>loweringAtSource</c>、<c>transferringAtSource</c>、<c>raisingAtSource</c>、<c>movingToDestination</c>、<c>loweringAtDestination</c>、<c>transferringAtDestination</c>、<c>raisingAtDestination</c>、<c>interrupted</c>。</param>
/// <param name="Paused">一時停止しているか。</param>
internal sealed record AgvTransportSnapshot(long Id, string From, string To, string Stage, bool Paused);

/// <summary>
/// 画面へ送る、1台の AGV の様子。言葉は画面の側と合わせる。並びは中身で比べる（変わったときだけ送るため）。
/// </summary>
/// <param name="Id">AGV の ID。</param>
/// <param name="Panel">いるパネル。位置がわからなければ null。</param>
/// <param name="Heading">向いている角度（0〜359、北が0で時計回り）。位置がわからなければ null。</param>
/// <param name="State">状態。<c>unregistered</c>（位置がわからない）、<c>resettingPosition</c>、<c>idle</c>、<c>moving</c>、<c>emergencyStopped</c>、<c>error</c>。</param>
/// <param name="Control">コントロールステータス。<c>auto</c>、<c>assignmentPaused</c>、<c>manual</c>、<c>locked</c>、<c>detached</c>。</param>
/// <param name="CarrierMoving">キャリアを上げ下げしている途中か。</param>
/// <param name="BatteryPercent">最後に届いた充電残量（%）。まだ届いていなければ null。</param>
internal sealed record AgvSnapshot(string Id, string? Panel, int? Heading, string State, string Control, bool CarrierMoving, int? BatteryPercent)
{
    /// <summary>予約しているパネル（いるパネルも含む）。</summary>
    public IReadOnlyList<string> ReservedPanels { get; init; } = [];

    /// <summary>走る予定の経路（今のパネルから行き先まで、通るパネルの順）。走る予定がなければ空。</summary>
    public IReadOnlyList<string> Route { get; init; } = [];

    /// <summary>割り当て済みの搬送。なければ null。</summary>
    public AgvTransportSnapshot? Transport { get; init; }

    /// <summary>交通整理の AGV の様子と、配車の割り当て済みの搬送を、画面へ送る形にする。</summary>
    /// <param name="assignment">その AGV に割り当てている搬送。なければ null。</param>
    /// <param name="endpointName">受け渡しの相手の参照を、画面に出す名前にする（消耗品の管理が書き方を知っている）。</param>
    public static AgvSnapshot From(AgvView view, AgvAssignment? assignment, Func<string, string> endpointName)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(endpointName);
        return new AgvSnapshot(
            view.Id.Value,
            view.Position?.NodeId,
            view.Position?.Heading,
            StateText(view.State),
            ControlText(view.Control),
            view.CarrierMoving,
            view.BatteryPercent)
        {
            ReservedPanels = [.. view.ReservedPanels.Select(p => p.Value)],
            Route = [.. view.RoutePanels.Select(p => p.Value)],
            Transport = assignment is null ? null : TransportOf(assignment, endpointName),
        };
    }

    public bool Equals(AgvSnapshot? other) =>
        other is not null
        && Id == other.Id
        && Panel == other.Panel
        && Heading == other.Heading
        && State == other.State
        && Control == other.Control
        && CarrierMoving == other.CarrierMoving
        && BatteryPercent == other.BatteryPercent
        && ReservedPanels.SequenceEqual(other.ReservedPanels)
        && Route.SequenceEqual(other.Route)
        && Equals(Transport, other.Transport);

    public override int GetHashCode() => HashCode.Combine(Id, Panel, Heading, State, Control, CarrierMoving, BatteryPercent, Transport);

    private static AgvTransportSnapshot TransportOf(AgvAssignment assignment, Func<string, string> endpointName) =>
        new(
            assignment.Transport.Value,
            TransportTexts.SourceName(assignment.Source, endpointName),
            endpointName(assignment.DestinationEndpoint),
            TransportTexts.Stage(assignment.Stage),
            assignment.Paused);

    private static string StateText(AgvState state) => state switch
    {
        AgvState.Unregistered => "unregistered",
        AgvState.ResettingPosition => "resettingPosition",
        AgvState.Idle => "idle",
        AgvState.Moving => "moving",
        AgvState.EmergencyStopped => "emergencyStopped",
        AgvState.Error => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "知らない AGV の状態です。"),
    };

    internal static string ControlText(ControlStatus control) => control switch
    {
        ControlStatus.Auto => "auto",
        ControlStatus.AssignmentPaused => "assignmentPaused",
        ControlStatus.Manual => "manual",
        ControlStatus.Locked => "locked",
        ControlStatus.Detached => "detached",
        _ => throw new ArgumentOutOfRangeException(nameof(control), control, "知らないコントロールステータスです。"),
    };
}
