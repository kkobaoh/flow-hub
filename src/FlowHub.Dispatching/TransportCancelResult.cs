namespace FlowHub.Dispatching;

/// <summary>
/// 搬送を取りやめた結果。
/// </summary>
public enum TransportCancelResult
{
    /// <summary>取りやめた（割り当て前なら待ち行列から外した。割り当てたあとなら AGV を止めて、取りやめを知らせた）。</summary>
    Canceled,

    /// <summary>取りやめを受け付けた。受け渡しの途中なので、キャリアを上げたところで止まり、取りやめを知らせる。</summary>
    Canceling,

    /// <summary>搬送先で受け渡しの許可を出したあとなので、最後まで終えて完了にする（ものが搬送先に届くため）。</summary>
    Completing,

    /// <summary>中断した、または終えた搬送なので、取りやめられない。</summary>
    NotCancelable,
}
