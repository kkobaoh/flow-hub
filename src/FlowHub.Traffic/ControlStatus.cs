namespace FlowHub.Traffic;

/// <summary>
/// AGV のコントロールステータス（システムが AGV をどう扱うか）。
/// </summary>
public enum ControlStatus
{
    /// <summary>オート。搬送を割り当て、システムの判断で動かす。</summary>
    Auto,

    /// <summary>割り当て停止。新しい搬送は割り当てないが、割り当て済みの搬送や充電は続ける。</summary>
    AssignmentPaused,

    /// <summary>マニュアル。システムの判断では動かさず、ユーザーの指示で動かす。全台の計画では障害物にする。</summary>
    Manual,

    /// <summary>ロック。緊急停止と位置リセットのほかは、コマンドを受け付けない。全台の計画では障害物にする。</summary>
    Locked,

    /// <summary>切り離し。どのコマンドも送らず、全台の計画にも入れない（予約も持たない）。</summary>
    Detached,
}
