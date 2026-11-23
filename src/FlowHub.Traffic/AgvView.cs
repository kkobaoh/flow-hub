using FlowHub.Map;
using FlowHub.Routing;

namespace FlowHub.Traffic;

/// <summary>
/// AGV の状態（ほかのコンテキストから見えるもの）。
/// </summary>
public enum AgvState
{
    /// <summary>位置がわからない。</summary>
    Unregistered,

    /// <summary>位置リセットの完了を待っている。</summary>
    ResettingPosition,

    /// <summary>待機中。</summary>
    Idle,

    /// <summary>走行中。</summary>
    Moving,

    /// <summary>緊急停止中。アラームクリアで続きを走れる。</summary>
    EmergencyStopped,

    /// <summary>異常。位置リセットが必要。</summary>
    Error,
}

/// <summary>
/// AGV の今の様子。
/// </summary>
/// <param name="Id">AGV の ID。</param>
/// <param name="Position">今の位置と向き。わからなければ null。</param>
/// <param name="State">状態。</param>
/// <param name="CarrierRaised">キャリアが上がっているか（下がっている間は走れない）。</param>
/// <param name="CumulativeDistanceMm">累計の走行距離。</param>
/// <param name="Control">コントロールステータス。</param>
public sealed record AgvView(AgvId Id, Pose? Position, AgvState State, bool CarrierRaised, long CumulativeDistanceMm, ControlStatus Control = ControlStatus.Auto)
{
    /// <summary>
    /// AGV が止まれる最初の位置（渡した区間は取り消せないので、走っていれば、その区間を走り終えたところ）。止まっていれば、今の位置。
    /// </summary>
    public Pose? StopsAt { get; init; } = Position;

    /// <summary>特殊消耗品受け渡しパネルを走れるか（AGV の種類ごとの設定）。</summary>
    public bool CanTravelSpecialTransfer { get; init; }

    /// <summary>キャリアを上げている途中か、下げている途中か。</summary>
    public bool CarrierMoving { get; init; }

    /// <summary>最後に AGV から届いた充電残量（%）。起動してから一度も届いていなければ null。</summary>
    public int? BatteryPercent { get; init; }

    /// <summary>予約しているパネル（いるパネルも含む）。</summary>
    public IReadOnlyList<PanelId> ReservedPanels { get; init; } = [];

    /// <summary>走る予定の経路（今のパネルから行き先まで、通るパネルの順）。AGV に渡した区間と、まだ渡していない区間をつなぐ。走る予定がなければ空。</summary>
    public IReadOnlyList<PanelId> RoutePanels { get; init; } = [];
}

/// <summary>
/// AGV を動かす頼みの結果。
/// </summary>
public enum MoveResult
{
    /// <summary>動かし始めた。着いたら <see cref="AgvArrived"/> で知らせる。</summary>
    Started,

    /// <summary>すでにそのパネルにいた。すぐに <see cref="AgvArrived"/> で知らせた。</summary>
    AlreadyThere,

    /// <summary>経路が見つからないので、受け付けなかった（画面で知らせる）。</summary>
    NoRoute,

    /// <summary>今は、ほかの AGV に塞がれていて行けない（全台の計画が立てられない）。その場で待ち、計画を立て直すたびに、また試す（行けるようになったら出発する）。</summary>
    Waiting,
}
