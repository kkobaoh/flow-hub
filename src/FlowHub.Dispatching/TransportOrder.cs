using FlowHub.Map;

namespace FlowHub.Dispatching;

/// <summary>
/// 受け渡しの向き。
/// </summary>
public enum TransferSide
{
    /// <summary>積み込み（搬送元で、相手からキャリアへ）。</summary>
    Load,

    /// <summary>降ろし（搬送先で、キャリアから相手へ）。</summary>
    Unload,
}

/// <summary>
/// 搬送の頼み（1往復ぶん）。配車は、運ぶものを知らない。
/// </summary>
/// <param name="Requester">頼んだ側。</param>
/// <param name="CargoRefs">運ぶものの参照。配車にとっては中身のわからない値。</param>
/// <param name="SourcePanel">搬送元の受け渡しパネル。</param>
/// <param name="SourceEndpoint">搬送元の受け渡しの相手（装置やステーション）の参照。</param>
/// <param name="DestinationPanel">搬送先の受け渡しパネル。</param>
/// <param name="DestinationEndpoint">搬送先の受け渡しの相手の参照。</param>
/// <param name="Carrier">求めるキャリアの種類。</param>
/// <param name="NeededBy">必要時刻。</param>
public sealed record TransportOrder(
    string Requester,
    IReadOnlyList<string> CargoRefs,
    PanelId SourcePanel,
    string SourceEndpoint,
    PanelId DestinationPanel,
    string DestinationEndpoint,
    CarrierTypeId Carrier,
    DateTimeOffset NeededBy);
