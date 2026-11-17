using FlowHub.Map;

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

    /// <summary>AGV をパネルへ動かす。着いたら <see cref="AgvArrived"/> で知らせる。</summary>
    /// <param name="correlationId">一連の流れの ID（例：搬送 ID）。このあとのイベントに付ける。</param>
    Task<MoveResult> MoveAgvAsync(AgvId id, PanelId destination, string correlationId, CancellationToken cancellationToken);

    /// <summary>キャリアを下げる。下がったら <see cref="CarrierLowered"/> で知らせる。</summary>
    Task LowerCarrierAsync(AgvId id, string correlationId, CancellationToken cancellationToken);

    /// <summary>キャリアを上げる。上がったら <see cref="CarrierRaised"/> で知らせる。</summary>
    Task RaiseCarrierAsync(AgvId id, string correlationId, CancellationToken cancellationToken);
}
