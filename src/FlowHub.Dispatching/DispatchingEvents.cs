using FlowHub.BuildingBlocks;
using FlowHub.Map;
using FlowHub.Traffic;

namespace FlowHub.Dispatching;

// 搬送のイベントには要求元（Requester）を入れる。各コンテキストは、自分が頼んだ搬送のイベントだけを処理する。

/// <summary>
/// 受け渡しの相手（装置やステーション）に、搬送の許可を出してほしい。受け渡しを終えたら、配車に知らせてもらう。
/// </summary>
public sealed record TransferPermissionRequested(
    EventMetadata Metadata,
    TransportId Transport,
    string Requester,
    IReadOnlyList<string> CargoRefs,
    string Endpoint,
    TransferSide Side) : IIntegrationEvent;

/// <summary>搬送が完了した。</summary>
public sealed record TransportCompleted(EventMetadata Metadata, TransportId Transport, string Requester) : IIntegrationEvent;

/// <summary>搬送を中断した（AGV の異常、または経路が見つからない）。画面で知らせる。</summary>
public sealed record TransportInterrupted(EventMetadata Metadata, TransportId Transport, string Requester) : IIntegrationEvent;

/// <summary>行ける充電ポートがない。空いたら向かわせる。画面で知らせる。</summary>
public sealed record NoChargingPortAvailable(EventMetadata Metadata, AgvId Agv) : IIntegrationEvent;

/// <summary>AGV を動かそうとしたが、予約が取れずに止まっている。画面で警告する。</summary>
public sealed record MoveBlocked(EventMetadata Metadata, AgvId Agv, PanelId Destination) : IIntegrationEvent;

/// <summary>システムの運転状態がスタートになった（搬送計画は、不足と回収の判断を1回行う）。</summary>
public sealed record OperationStarted(EventMetadata Metadata) : IIntegrationEvent;
