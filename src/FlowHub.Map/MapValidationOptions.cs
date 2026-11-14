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
}
