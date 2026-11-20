using FlowHub.Map;

namespace FlowHub.Api.InitialData;

// 開発用の初期データの JSON ファイルの形（initial-data.json）。読むためだけの入れ物なので、検証は InitialDataSeeder が行う。

internal sealed class InitialDataFile
{
    public MapData? Map { get; set; }

    public List<AgvTypeData> AgvTypes { get; set; } = [];

    public List<AgvData> Agvs { get; set; } = [];

    public ConsumablesData? Consumables { get; set; }
}

internal sealed class MapData
{
    public List<PanelData> Panels { get; set; } = [];

    public List<ConnectionData> Connections { get; set; } = [];
}

internal sealed class PanelData
{
    public string Id { get; set; } = string.Empty;

    public PanelType Type { get; set; }

    public bool ChargingPin { get; set; }

    /// <summary>キャリアを下ろす角度（特殊消耗品受け渡しパネルだけ）。</summary>
    public int? CarrierLoweringHeading { get; set; }
}

internal sealed class ConnectionData
{
    public string From { get; set; } = string.Empty;

    public string To { get; set; } = string.Empty;

    public int DistanceMm { get; set; }

    public int Heading { get; set; }

    public bool OneWay { get; set; }
}

internal sealed class AgvTypeData
{
    public string Id { get; set; } = string.Empty;

    public double ForwardSpeedMmPerSecond { get; set; }

    public double BackwardSpeedMmPerSecond { get; set; }

    public double RotationSpeedDegreesPerSecond { get; set; }

    /// <summary>充電に向かう値。省略したら既定値。</summary>
    public int? GoChargeBelowPercent { get; set; }

    /// <summary>割り当てに戻す値。省略したら既定値。</summary>
    public int? ResumeAtPercent { get; set; }
}

internal sealed class AgvData
{
    public string Id { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Carrier { get; set; } = string.Empty;
}

internal sealed class ConsumablesData
{
    public List<ProcessTypeData> ProcessTypes { get; set; } = [];

    public List<VarietyData> Varieties { get; set; } = [];

    public List<DeviceData> Devices { get; set; } = [];

    public List<StationData> Stations { get; set; } = [];

    public List<StockerSettingData> StockerSettings { get; set; } = [];

    public List<ExpectedArrivalData> ExpectedArrivals { get; set; } = [];
}

internal sealed class ProcessTypeData
{
    public string Id { get; set; } = string.Empty;

    public string Carrier { get; set; } = string.Empty;

    public int CarrierCapacity { get; set; }
}

internal sealed class VarietyData
{
    public string Id { get; set; } = string.Empty;

    public string ProcessType { get; set; } = string.Empty;
}

internal sealed class DeviceData
{
    public string Id { get; set; } = string.Empty;

    public string TransferPanel { get; set; } = string.Empty;

    public List<int> SlotsPerStocker { get; set; } = [];
}

internal sealed class StationData
{
    public string Id { get; set; } = string.Empty;

    public string TransferPanel { get; set; } = string.Empty;
}

internal sealed class StockerSettingData
{
    public string Device { get; set; } = string.Empty;

    public Dictionary<string, int> Counts { get; set; } = [];
}

internal sealed class ExpectedArrivalData
{
    public string Consumable { get; set; } = string.Empty;

    public string Variety { get; set; } = string.Empty;
}
