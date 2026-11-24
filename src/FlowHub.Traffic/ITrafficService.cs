using FlowHub.Map;
using FlowHub.Routing;

namespace FlowHub.Traffic;

/// <summary>
/// 交通整理の窓口のうち、配車などほかのコンテキストから使うもの。
/// </summary>
public interface ITrafficService
{
    /// <summary>AGV の今の様子。</summary>
    AgvView GetAgv(AgvId id);

    /// <summary>AGV の今の位置から、パネルまでの移動コスト（所要時間）。経路が見つからなければ null。予約は考えない。</summary>
    TimeSpan? EstimateMoveCost(AgvId id, PanelId destination);

    /// <summary>ほかの AGV がそのパネルを予約しているか。</summary>
    bool IsReservedByOther(PanelId panel, AgvId self);

    /// <summary>
    /// AGV をパネルへ動かす。全台の経路を計画し直し、パネルごとの通る順番を守らせて走らせる。着いたら <see cref="AgvArrived"/> で知らせる。
    /// </summary>
    /// <param name="neededBy">搬送の必要時刻（計画の順番に使う。早いほど先）。搬送のない移動（充電に向かうなど）は null で、搬送のあとにする。</param>
    /// <param name="correlationId">一連の流れの ID（例：搬送 ID）。このあとのイベントに付ける。</param>
    Task<MoveResult> MoveAgvAsync(AgvId id, PanelId destination, DateTimeOffset? neededBy, string correlationId, CancellationToken cancellationToken);

    /// <summary>
    /// AGV を止める（搬送の取りやめなど）。行き先とまだ渡していないコマンドを取り下げ、AGV が受け取ったコマンドを終えたところで止まる。
    /// 止まったパネル以外の予約を外す。着いたことは知らせない。
    /// </summary>
    Task StopAgvAsync(AgvId id, string correlationId, CancellationToken cancellationToken);

    /// <summary>キャリアを下げる。下がったら <see cref="CarrierLowered"/> で知らせる。</summary>
    Task LowerCarrierAsync(AgvId id, string correlationId, CancellationToken cancellationToken);

    /// <summary>キャリアを上げる。上がったら <see cref="CarrierRaised"/> で知らせる。</summary>
    Task RaiseCarrierAsync(AgvId id, string correlationId, CancellationToken cancellationToken);

    /// <summary>
    /// 通り道を塞いでいる行き先のない AGV を、待機パネルへどかしてよいか（システムの運転状態が停止の間は止める）。切り替えたら、全台の経路を計画し直す。
    /// </summary>
    void SetEvacuationEnabled(bool enabled);

    /// <summary>
    /// コントロールステータスを切り替える。切り替えたら、全台の経路を計画し直す。
    /// マニュアルとロックに切り替えるときは、行き先と残りの経路を捨てる（着いたことは知らせない）。
    /// </summary>
    /// <param name="position">切り離しから切り替えるときに必須の位置。位置リセットを送り、完了の通知を受けたら切り替わる。</param>
    /// <returns>切り替えを受け付けたか。位置のパネルをほかの AGV が予約していたら false。</returns>
    /// <exception cref="InvalidOperationException">切り替えの条件を満たさないとき。</exception>
    /// <exception cref="ArgumentException">切り離しから切り替えるのに、位置を指定していないとき。</exception>
    Task<bool> ChangeControlStatusAsync(AgvId id, ControlStatus target, Pose? position, CancellationToken cancellationToken);
}
