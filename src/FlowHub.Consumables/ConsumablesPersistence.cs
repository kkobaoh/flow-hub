namespace FlowHub.Consumables;

/// <summary>
/// 消耗品の管理の、実行中に変わる構成のデータ（ストッカーの設定、入庫の予定）の保存先。再起動しても残すために使う。
/// </summary>
public interface IConsumablesPersistence
{
    /// <summary>装置のストッカーの設定を保存する（前の設定と置き換える。空の設定も「設定あり」として残す）。</summary>
    void SaveStockerSetting(DeviceId device, IReadOnlyDictionary<VarietyId, int> counts);

    /// <summary>入庫の予定（ID と品種の対応）を保存する。</summary>
    void AddExpectedArrival(ConsumableId consumable, VarietyId variety);

    /// <summary>入庫の予定を消す（入庫したとき）。</summary>
    void RemoveExpectedArrival(ConsumableId consumable);
}

/// <summary>
/// 何も保存しない（DB を使わないとき。再起動すると消える）。
/// </summary>
public sealed class NoConsumablesPersistence : IConsumablesPersistence
{
    public static readonly NoConsumablesPersistence Instance = new();

    private NoConsumablesPersistence()
    {
    }

    public void SaveStockerSetting(DeviceId device, IReadOnlyDictionary<VarietyId, int> counts)
    {
    }

    public void AddExpectedArrival(ConsumableId consumable, VarietyId variety)
    {
    }

    public void RemoveExpectedArrival(ConsumableId consumable)
    {
    }
}

/// <summary>品種と、その品種が属する加工の種類。</summary>
public sealed record VarietyConfiguration(VarietyId Variety, ProcessTypeId ProcessType);

/// <summary>装置のストッカーの設定（品種ごとにストックしておく個数）。</summary>
public sealed record StockerSettingConfiguration(DeviceId Device, IReadOnlyDictionary<VarietyId, int> Counts);

/// <summary>入庫の予定（前もって登録された ID と品種の対応）。</summary>
public sealed record ExpectedArrival(ConsumableId Consumable, VarietyId Variety);

/// <summary>
/// 起動するときに読み込む、消耗品の管理の構成のデータ。
/// </summary>
public sealed record ConsumablesConfiguration(
    IReadOnlyList<DeviceLayout> Devices,
    IReadOnlyList<StationLayout> Stations,
    IReadOnlyList<ProcessTypeSettings> ProcessTypes,
    IReadOnlyList<VarietyConfiguration> Varieties,
    IReadOnlyList<StockerSettingConfiguration> StockerSettings,
    IReadOnlyList<ExpectedArrival> ExpectedArrivals);
