namespace FlowHub.Dispatching;

/// <summary>
/// キャリアの種類の ID。消耗品搬送専用の AGV は、運べる加工の種類がキャリアで決まる。
/// 配車は、運ぶものを知らずに、搬送の要求が求めるキャリアの種類と AGV のキャリアの種類を比べるだけ。
/// </summary>
public readonly record struct CarrierTypeId
{
    public CarrierTypeId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
