namespace FlowHub.Map;

/// <summary>
/// マップ上の絶対角度（1度単位）。0度以上360度未満。
/// </summary>
public readonly record struct Heading
{
    private const int FullCircle = 360;

    public Heading(int degrees)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(degrees);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(degrees, FullCircle);
        Degrees = degrees;
    }

    public int Degrees { get; }

    /// <summary>
    /// 180度反対の向き。
    /// </summary>
    public Heading Reverse() => new((Degrees + (FullCircle / 2)) % FullCircle);
}
