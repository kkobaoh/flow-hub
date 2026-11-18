using FlowHub.Map;

namespace FlowHub.Consumables;

/// <summary>
/// 回収する理由。回収の往復に積む順に並べている。
/// </summary>
public enum RecoveryReason
{
    /// <summary>設定にない品種の新品。</summary>
    NotInSetting,

    /// <summary>設定より多い品種のせいで、ほかの品種の不足を補う空きが足りない。</summary>
    Surplus,

    /// <summary>装置から廃棄リクエストを受けた。</summary>
    Discard,
}

/// <summary>
/// 回収が必要な消耗品。
/// </summary>
public sealed record DeviceRecovery(ConsumableId Consumable, VarietyId Variety, ProcessTypeId ProcessType, RecoveryReason Reason);

/// <summary>
/// 装置の不足と回収の判断の結果。
/// </summary>
/// <param name="Shortages">不足している品種と個数（届ける途中のものを数えたうえで）。</param>
/// <param name="Recoveries">新たに回収が必要な消耗品（回収を頼んだものは入らない）。</param>
/// <param name="FreeSlots">届ける要求を出せる数。今空いているスロットの数から、届ける途中の数を引いた数（0未満にはしない）。</param>
public sealed record DeviceNeeds(IReadOnlyDictionary<VarietyId, int> Shortages, IReadOnlyList<DeviceRecovery> Recoveries, int FreeSlots);

/// <summary>
/// ステーションにある、届けられる消耗品。
/// </summary>
public sealed record StationStock(ConsumableId Consumable, StationId Station);

/// <summary>
/// 配車に渡す受け渡しの場所（受け渡しパネルと、受け渡しの相手の参照）。
/// </summary>
public sealed record TransferEndpoint(PanelId Panel, string Endpoint);
