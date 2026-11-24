namespace FlowHub.Map;

/// <summary>
/// パネルの格子の位置。列は東へ、行は南へ増える（docs/domain.md「パネルとマップ」）。
/// </summary>
public sealed record GridPosition(int Column, int Row);

/// <summary>
/// 十字の交点の、パネルの真ん中からのずれ（mm）。東と南を正にする。ずれていなければ 0。
/// </summary>
public sealed record CrossOffset(int EastMm, int SouthMm)
{
    /// <summary>ずれていない。</summary>
    public static CrossOffset None { get; } = new(0, 0);
}

/// <summary>
/// 受け渡しパネルの下（床の下）にある装置かステーションの外形（mm。マップの左上を原点にし、東と南を正にする）。
/// </summary>
public sealed record Outline
{
    public Outline(int xMm, int yMm, int widthMm, int heightMm)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthMm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightMm);
        XMm = xMm;
        YMm = yMm;
        WidthMm = widthMm;
        HeightMm = heightMm;
    }

    public int XMm { get; }

    public int YMm { get; }

    /// <summary>東西の幅（1 以上）。</summary>
    public int WidthMm { get; }

    /// <summary>南北の奥行き（1 以上）。</summary>
    public int HeightMm { get; }
}

/// <summary>
/// 1枚のパネルの描き方：格子の位置、十字のずれ、受け渡しパネルなら下にあるものの外形（なければ null）。
/// </summary>
public sealed record PanelPlacement(GridPosition Position, CrossOffset CrossOffset, Outline? Outline);

/// <summary>
/// 画面に描くための情報。走行の判断（経路、予約）には使わない。
/// </summary>
public sealed class MapDrawing
{
    public MapDrawing(int panelSizeMm, IReadOnlyDictionary<PanelId, PanelPlacement> placements)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(panelSizeMm);
        ArgumentNullException.ThrowIfNull(placements);
        PanelSizeMm = panelSizeMm;
        Placements = new Dictionary<PanelId, PanelPlacement>(placements);
    }

    /// <summary>パネルの一辺の長さ（mm）。マップ全体で1つ。</summary>
    public int PanelSizeMm { get; }

    /// <summary>パネルごとの描き方。</summary>
    public IReadOnlyDictionary<PanelId, PanelPlacement> Placements { get; }

    /// <summary>パネルの十字の交点（mm。マップの左上を原点にし、東と南を正にする）。</summary>
    public (double X, double Y) CrossPoint(PanelId panel)
    {
        var p = Placements[panel];
        return (((p.Position.Column + 0.5) * PanelSizeMm) + p.CrossOffset.EastMm, ((p.Position.Row + 0.5) * PanelSizeMm) + p.CrossOffset.SouthMm);
    }
}
