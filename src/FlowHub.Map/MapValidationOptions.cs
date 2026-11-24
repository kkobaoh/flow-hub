namespace FlowHub.Map;

/// <summary>
/// マップの検証のオプション。現場の運用に合わせて、追加の検証を選べる。
/// </summary>
public sealed record MapValidationOptions
{
    /// <summary>
    /// 既定のオプション（追加の検証をしない）。
    /// </summary>
    public static MapValidationOptions Default { get; } = new();

    /// <summary>
    /// すべてのパネルが1つにつながっていること（離れ島がないこと）を求めるか。既定は false（離れ島を許す）。
    /// 一方通行のつながりも、つながりとして数える。
    /// </summary>
    public bool RequireConnected { get; init; }

    /// <summary>上下左右に隣り合うパネルの、十字の交点の間の最小の距離（mm）の既定値。</summary>
    public const int DefaultMinimumCrossDistanceMm = 650;

    private readonly int _minimumCrossDistanceMm = DefaultMinimumCrossDistanceMm;

    /// <summary>
    /// 上下左右に隣り合うパネルの、十字の交点の間の最小の距離（mm。1 以上）。隣り合うパネルにそれぞれ止まった AGV が、ぶつからないための距離。
    /// 描く情報（<see cref="MapDrawing"/>）があるときに、位置から計算した距離と、つながりの距離の両方で確かめる。ちょうどの値はよい。
    /// </summary>
    public int MinimumCrossDistanceMm
    {
        get => _minimumCrossDistanceMm;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _minimumCrossDistanceMm = value;
        }
    }
}
