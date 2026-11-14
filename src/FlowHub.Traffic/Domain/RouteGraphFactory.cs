using FlowHub.Map;
using FlowHub.Routing;

namespace FlowHub.Traffic.Domain;

/// <summary>
/// マップのスナップショットを、ルーティングの入力にするグラフに変換する。
/// ルーティングを置き換えるときは、この変換を差し替える。
/// </summary>
internal static class RouteGraphFactory
{
    public static RouteGraph FromMap(MapSnapshot map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var nodes = map.Panels.Select(ToNode);
        var edges = map.Connections.Select(c => new RouteEdge(c.From.Value, c.To.Value, c.DistanceMm, c.Heading.Degrees, c.OneWay));

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
