using FlowHub.Traffic;

namespace FlowHub.Api.Composition.Screens;

/// <summary>
/// 画面へ送る、すべての AGV の様子。変わるたびに番号を進める。
/// </summary>
/// <param name="Version">様子の番号。1から始まり、変わるたびに1つ進む。</param>
/// <param name="Agvs">AGV ごとの様子（ID の順）。</param>
internal sealed record AgvsSnapshot(long Version, IReadOnlyList<AgvSnapshot> Agvs);

/// <summary>
/// 画面へ送る、1台の AGV の様子。言葉は画面の側と合わせる。
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
    /// <summary>交通整理の AGV の様子を、画面へ送る形にする。</summary>
    public static AgvSnapshot From(AgvView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new AgvSnapshot(
            view.Id.Value,
            view.Position?.NodeId,
            view.Position?.Heading,
            StateText(view.State),
            ControlText(view.Control),
            view.CarrierMoving,
            view.BatteryPercent);
    }

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

    private static string ControlText(ControlStatus control) => control switch
    {
        ControlStatus.Auto => "auto",
        ControlStatus.AssignmentPaused => "assignmentPaused",
        ControlStatus.Manual => "manual",
        ControlStatus.Locked => "locked",
        ControlStatus.Detached => "detached",
        _ => throw new ArgumentOutOfRangeException(nameof(control), control, "知らないコントロールステータスです。"),
    };
}
