using FlowHub.BuildingBlocks;
using FlowHub.Map;

namespace FlowHub.Traffic;

/// <summary>AGV が頼まれたパネルに着いた。</summary>
public sealed record AgvArrived(EventMetadata Metadata, AgvId Agv, PanelId Panel) : IIntegrationEvent;

/// <summary>キャリアが下がった。</summary>
public sealed record CarrierLowered(EventMetadata Metadata, AgvId Agv) : IIntegrationEvent;

/// <summary>キャリアが上がった。</summary>
public sealed record CarrierRaised(EventMetadata Metadata, AgvId Agv) : IIntegrationEvent;

/// <summary>AGV が緊急停止中になった（緊急停止、または位置はずれていない種類のエラー）。</summary>
public sealed record AgvStopped(EventMetadata Metadata, AgvId Agv) : IIntegrationEvent;

/// <summary>AGV のアラームクリアが完了し、続きを走れるようになった。</summary>
public sealed record AgvResumed(EventMetadata Metadata, AgvId Agv) : IIntegrationEvent;

/// <summary>AGV が「異常」になった（位置リセットが必要）。</summary>
public sealed record AgvFaulted(EventMetadata Metadata, AgvId Agv) : IIntegrationEvent;

/// <summary>AGV の充電残量が変わった。</summary>
public sealed record AgvBatteryChanged(EventMetadata Metadata, AgvId Agv, int Percent) : IIntegrationEvent;
