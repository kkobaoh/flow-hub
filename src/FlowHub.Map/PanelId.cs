namespace FlowHub.Map;

/// <summary>
/// パネルの ID。
/// </summary>
public readonly record struct PanelId
{
    public PanelId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
