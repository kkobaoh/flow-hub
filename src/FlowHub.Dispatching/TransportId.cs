namespace FlowHub.Dispatching;

/// <summary>
/// 搬送の ID。頼まれた順に 1 から付ける。
/// </summary>
public readonly record struct TransportId(long Value)
{
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
