namespace FlowHub.Consumables;

/// <summary>
/// 消耗品の個体の ID。
/// </summary>
public readonly record struct ConsumableId
{
    public ConsumableId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// 消耗品の状態（新品か中古品か）。加工部に取り込まれた時点で中古品になる。
/// </summary>
public enum ConsumableCondition
{
    New,
    Used,
}

/// <summary>
/// 装置の ID。
/// </summary>
public readonly record struct DeviceId
{
    public DeviceId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// ステーションの ID。
/// </summary>
public readonly record struct StationId
{
    public StationId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// 加工の種類の ID。
/// </summary>
public readonly record struct ProcessTypeId
{
    public ProcessTypeId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// 品種の ID。互換性は品種の中だけで、品種の間に互換性はない。
/// </summary>
public readonly record struct VarietyId
{
    public VarietyId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
