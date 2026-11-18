using FlowHub.BuildingBlocks;

namespace FlowHub.Consumables;

/// <summary>
/// 不足と回収の判断にかかわる在庫が変わった（設定の変更、入庫、廃棄リクエスト、受け渡しの完了）。搬送計画が判断をやり直す。
/// </summary>
public sealed record StockChanged(EventMetadata Metadata) : IIntegrationEvent;
