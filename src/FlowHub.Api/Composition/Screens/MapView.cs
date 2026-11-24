using FlowHub.Map;

namespace FlowHub.Api.Composition.Screens;

/// <summary>
/// 画面へ送るマップの様子の元（公開中の版と、その上に描く装置とステーション）。
/// </summary>
/// <param name="MapVersion">公開中のマップの版の番号（まだ公開していなければ 0）。</param>
/// <param name="Map">公開中のマップ（まだ公開していなければ null）。</param>
/// <param name="Equipment">マップに描く装置とステーション（外形のあるものだけ）。</param>
internal sealed record ScreenMapState(int MapVersion, MapSnapshot? Map, IReadOnlyList<EquipmentView> Equipment)
{
    /// <summary>まだマップがない。</summary>
    public static ScreenMapState Empty { get; } = new(0, null, []);

    /// <summary>同じ版で、同じ装置とステーションなら同じ（版が同じなら、マップの中身も同じ）。</summary>
    public bool SameAs(ScreenMapState other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return MapVersion == other.MapVersion && Equipment.SequenceEqual(other.Equipment);
    }

    /// <summary>画面へ送る形にする。</summary>
    /// <param name="version">様子の番号。</param>
    public MapViewSnapshot ToSnapshot(long version)
    {
        var drawing = Map?.Drawing;
        return new MapViewSnapshot(
            version,
            MapVersion,
            drawing?.PanelSizeMm ?? 0,
            Map is null || drawing is null ? [] : [.. Map.Panels.Select(p => PanelView.From(p, drawing.Placements[p.Id]))],
            Map is null ? [] : [.. Map.Connections.Select(c => new ConnectionView(c.From.Value, c.To.Value, c.DistanceMm, c.Heading.Degrees, c.OneWay))],
            Equipment);
    }
}

/// <summary>
/// 画面へ送るマップの様子。公開中の版が変わるか、装置とステーションが変わるたびに番号を進める。
/// </summary>
/// <param name="Version">様子の番号。1から始まり、変わるたびに1つ進む。</param>
/// <param name="MapVersion">公開中のマップの版の番号（まだ公開していなければ 0。そのときパネルは空）。</param>
internal sealed record MapViewSnapshot(
    long Version,
    int MapVersion,
    int PanelSizeMm,
    IReadOnlyList<PanelView> Panels,
    IReadOnlyList<ConnectionView> Connections,
    IReadOnlyList<EquipmentView> Equipment);

/// <summary>画面へ送るパネル。種類は <see cref="PanelType"/> の名前（Normal、Transfer、Standby、SpecialTransfer）。</summary>
internal sealed record PanelView(
    string Id,
    string Type,
    bool HasChargingPin,
    int? CarrierLoweringHeading,
    int Column,
    int Row,
    int CrossOffsetEastMm,
    int CrossOffsetSouthMm)
{
    public static PanelView From(Panel panel, PanelPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(placement);
        return new PanelView(
            panel.Id.Value,
            panel.Type.ToString(),
            panel.HasChargingPin,
            panel.CarrierLoweringHeading?.Degrees,
            placement.Position.Column,
            placement.Position.Row,
            placement.CrossOffset.EastMm,
            placement.CrossOffset.SouthMm);
    }
}

/// <summary>画面へ送るつながり。向きは From から To へ進むときの向き（北が0度、時計回り）。</summary>
internal sealed record ConnectionView(string From, string To, int DistanceMm, int Heading, bool OneWay);

/// <summary>画面へ送る装置かステーション。</summary>
/// <param name="Kind"><c>"device"</c>（装置）か <c>"station"</c>（ステーション）。</param>
/// <param name="Panel">受け渡しパネル。</param>
/// <param name="Outline">外形（mm。マップの左上を原点にし、東と南を正にする）。</param>
internal sealed record EquipmentView(string Id, string Kind, string Panel, OutlineView Outline);

/// <summary>画面へ送る外形。</summary>
internal sealed record OutlineView(int XMm, int YMm, int WidthMm, int HeightMm)
{
    public static OutlineView From(Outline outline)
    {
        ArgumentNullException.ThrowIfNull(outline);
        return new OutlineView(outline.XMm, outline.YMm, outline.WidthMm, outline.HeightMm);
    }
}
