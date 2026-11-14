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
