namespace FlowHub.Traffic;

/// <summary>
/// AGV の ID。
/// </summary>
public readonly record struct AgvId
{
    public AgvId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
