using FlowHub.Map;
using FlowHub.Traffic;

namespace FlowHub.Dispatching.Domain;

/// <summary>
/// 受け渡しをする場所。受け渡しパネルと、その先の相手（装置やステーション）。
/// </summary>
/// <param name="Panel">受け渡しパネル。</param>
/// <param name="Endpoint">受け渡しの相手（装置やステーション）の参照。配車にとっては中身のわからない値。</param>
internal sealed record TransferPoint(PanelId Panel, string Endpoint);

/// <summary>
/// 搬送の要求（1往復ぶん）。配車は、運ぶものを知らない。
/// </summary>
/// <param name="Requester">頼んだ側。</param>
/// <param name="CargoRefs">運ぶものの参照。配車にとっては中身のわからない値。</param>
/// <param name="Source">搬送元（積み込む場所）。</param>
/// <param name="Destination">搬送先（降ろす場所）。</param>
/// <param name="Carrier">求めるキャリアの種類。</param>
/// <param name="NeededBy">必要時刻。待ち行列の並べ方に使う。</param>
internal sealed record TransportRequest(
    string Requester,
    IReadOnlyList<string> CargoRefs,
    TransferPoint Source,
    TransferPoint Destination,
    CarrierTypeId Carrier,
    DateTimeOffset NeededBy);

/// <summary>
/// 搬送を進めるために、配車がほかのコンテキストに頼むことや知らせること。
/// </summary>
internal abstract record TransportAction;

/// <summary>交通整理に頼む：AGV をパネルへ動かす。</summary>
internal sealed record MoveAgv(AgvId Agv, PanelId To) : TransportAction;

/// <summary>交通整理に頼む：キャリアを下げる（高さは交通整理がマップの設定から読む）。</summary>
internal sealed record LowerCarrier(AgvId Agv) : TransportAction;

/// <summary>交通整理に頼む：キャリアを上げる。</summary>
internal sealed record RaiseCarrier(AgvId Agv) : TransportAction;

/// <summary>イベントで知らせる：受け渡しの相手に、搬送の許可を出してほしい。</summary>
internal sealed record RequestTransferPermission(TransportId Transport, TransferPoint At, TransferSide Side) : TransportAction;

/// <summary>イベントで知らせる：搬送が完了した。</summary>
internal sealed record NotifyCompleted(TransportId Transport) : TransportAction;

/// <summary>イベントで知らせる：搬送を中断した（画面で知らせる）。</summary>
internal sealed record NotifyInterrupted(TransportId Transport) : TransportAction;
