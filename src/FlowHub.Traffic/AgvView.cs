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
public sealed record AgvView(AgvId Id, Pose? Position, AgvState State, bool CarrierRaised, long CumulativeDistanceMm);

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
