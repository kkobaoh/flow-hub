namespace FlowHub.Routing;

/// <summary>
/// 絶対角度（1度単位、0度以上360度未満）の計算。
/// </summary>
internal static class Angles
{
    private const int FullCircle = 360;

    public static void ThrowIfOutOfRange(int degrees, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(degrees))] string? paramName = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(degrees, paramName);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(degrees, FullCircle, paramName);
    }

    public static int Reverse(int degrees) => (degrees + (FullCircle / 2)) % FullCircle;

    /// <summary>
    /// 2つの角度の差のうち、小さいほう（近い回りで回るときの角度。0度以上180度以下）。
    /// </summary>
    public static int Difference(int a, int b)
    {
        var diff = Math.Abs(a - b) % FullCircle;
        return Math.Min(diff, FullCircle - diff);
    }
}
