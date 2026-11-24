using FlowHub.Map;
using FlowHub.Routing;

namespace FlowHub.Traffic.Domain;

/// <summary>
/// マップのスナップショットを、ルーティングの入力にするグラフに変換する。
/// ルーティングを置き換えるときは、この変換を差し替える。
/// </summary>
internal static class RouteGraphFactory
{
    /// <param name="canTravelSpecialTransfer">特殊消耗品受け渡しパネルを走れるか。走れなければ、そのパネルと、そこへのつながりを取り除く
    /// （ルーティングの入力に「避けるパネル」は含めず、呼び出す側がグラフから取り除く）。</param>
    public static RouteGraph FromMap(MapSnapshot map, bool canTravelSpecialTransfer = true)
    {
        ArgumentNullException.ThrowIfNull(map);

        var removed = canTravelSpecialTransfer
            ? []
            : map.Panels.Where(p => p.Type == PanelType.SpecialTransfer).Select(p => p.Id).ToHashSet();
        var nodes = map.Panels.Where(p => !removed.Contains(p.Id)).Select(ToNode);
        var edges = map.Connections
            .Where(c => !removed.Contains(c.From) && !removed.Contains(c.To))
            .Select(c => new RouteEdge(c.From.Value, c.To.Value, c.DistanceMm, c.Heading.Degrees, c.OneWay));

        return new RouteGraph(nodes, edges);
    }

    /// <summary>
    /// 受け渡しパネルと待機パネルは行き止まり：前進でのみ入れて、後退でのみ出られる。中では回転できない。
    /// </summary>
    private static RouteNode ToNode(Panel panel) =>
        new(
            panel.Id.Value,
            CanRotate: !panel.IsDeadEnd,
            ForwardEntryOnly: panel.IsDeadEnd,
            BackwardExitOnly: panel.IsDeadEnd);
}
