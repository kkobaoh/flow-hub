using System.Globalization;

namespace FlowHub.Map.Domain;

/// <summary>
/// 画面に描くための情報の検証（docs/domain.md「パネルとマップ」）。違反をすべて集める。
/// </summary>
internal static class MapDrawingValidator
{
    /// <summary>上下左右の隣（東と南だけを見れば、すべての組を1回ずつ見られる）。</summary>
    private static readonly (int Column, int Row)[] EastAndSouth = [(1, 0), (0, 1)];

    public static void Validate(
        Dictionary<PanelId, Panel> panelsById,
        List<Connection> connections,
        MapDrawing drawing,
        int minimumCrossDistanceMm,
        List<MapValidationError> errors)
    {
        foreach (var panel in panelsById.Keys.Where(id => !drawing.Placements.ContainsKey(id)).OrderBy(id => id.Value, StringComparer.Ordinal))
        {
            errors.Add(new(MapValidationErrorCode.MissingPlacement, $"パネル {panel} の位置がありません。"));
        }

        foreach (var panel in drawing.Placements.Keys.Where(id => !panelsById.ContainsKey(id)).OrderBy(id => id.Value, StringComparer.Ordinal))
        {
            errors.Add(new(MapValidationErrorCode.PlacementForUnknownPanel, $"マップにないパネル {panel} の位置があります。"));
        }

        // ここから先は、マップにあって位置のあるパネルだけを見る
        var placed = drawing.Placements.Where(p => panelsById.ContainsKey(p.Key)).ToDictionary(p => p.Key, p => p.Value);

        var byCell = new Dictionary<GridPosition, PanelId>();
        foreach (var (panel, placement) in placed.OrderBy(p => p.Key.Value, StringComparer.Ordinal))
        {
            if (!byCell.TryAdd(placement.Position, panel))
            {
                errors.Add(new(
                    MapValidationErrorCode.DuplicateGridPosition,
                    $"パネル {byCell[placement.Position]} と {panel} が、同じ格子（列 {placement.Position.Column}、行 {placement.Position.Row}）にあります。"));
            }

            if (placement.Outline is not null && panelsById[panel].Type is not (PanelType.Transfer or PanelType.SpecialTransfer))
            {
                errors.Add(new(MapValidationErrorCode.OutlineOnNonTransferPanel, $"パネル {panel} は受け渡しパネルではないので、外形を置けません。"));
            }
        }

        ValidateCrossDistances(byCell, connections, drawing, minimumCrossDistanceMm, errors);
    }

    /// <summary>
    /// 上下左右に隣り合うすべての組で、十字の交点の間（位置から計算した距離）が最小の距離以上かを確かめる。
    /// つながっている組は、つながりの距離（実測）も確かめる。
    /// </summary>
    private static void ValidateCrossDistances(
        Dictionary<GridPosition, PanelId> byCell,
        List<Connection> connections,
        MapDrawing drawing,
        int minimumMm,
        List<MapValidationError> errors)
    {
        foreach (var (cell, panel) in byCell.OrderBy(c => c.Value.Value, StringComparer.Ordinal))
        {
            foreach (var (dc, dr) in EastAndSouth)
            {
                if (!byCell.TryGetValue(new GridPosition(cell.Column + dc, cell.Row + dr), out var neighbor))
                {
                    continue;
                }

                var a = drawing.CrossPoint(panel);
                var b = drawing.CrossPoint(neighbor);
                var distance = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
                if (distance < minimumMm)
                {
                    errors.Add(new(
                        MapValidationErrorCode.CrossDistanceTooShort,
                        $"パネル {panel} と {neighbor} の十字の交点の間が {Math.Round(distance).ToString(CultureInfo.InvariantCulture)}mm で、{minimumMm}mm より短くなっています。十字のずれを確かめてください。"));
                }

                var connection = connections.FirstOrDefault(c => (c.From == panel && c.To == neighbor) || (c.From == neighbor && c.To == panel));
                if (connection is not null && connection.DistanceMm < minimumMm)
                {
                    errors.Add(new(
                        MapValidationErrorCode.ConnectionDistanceTooShort,
                        $"パネル {panel} と {neighbor} のつながりの距離が {connection.DistanceMm}mm で、{minimumMm}mm より短くなっています。"));
                }
            }
        }
    }
}
