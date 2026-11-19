using FlowHub.Map;
using FlowHub.PathPlanning;

namespace FlowHub.Traffic.Domain;

/// <summary>
/// マップのスナップショットを、全台の経路の計画（PathPlanning）の入力の形に変える。
/// </summary>
internal static class PlanningMapFactory
{
    public static PlanningMap FromMap(MapSnapshot map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return new PlanningMap(
            map.Panels.Select(panel => new PlanningNode(
                panel.Id.Value,
                CanRotate: !panel.IsDeadEnd,
                ForwardEntryOnly: panel.IsDeadEnd,
                BackwardExitOnly: panel.IsDeadEnd)),
            map.Connections.Select(c => new PlanningEdge(c.From.Value, c.To.Value, c.DistanceMm, c.Heading.Degrees, c.OneWay)));
    }
}

/// <summary>
/// 全台の計画の「パネルごとの通る順番」を守らせる。区間を渡す前に、その区間のパネルを、
/// 自分より先に通る予定の AGV がすべて通り終えているかを確かめる。
/// </summary>
/// <remarks>
/// 通り終えたかは、通る順番に並んでいる回数と、その AGV がこれからそのパネルを使う回数（残りの経路）の差で数える。
/// 同じパネルを2回通る AGV も扱える。計画にない AGV（動かせない障害物など）は、通り終えることがないものとする。
/// </remarks>
/// <param name="remainingPanelsOf">AGV がこれから使うパネルの並び。わからなければ null。</param>
internal sealed class PassingOrderGate(Func<AgvId, IReadOnlyList<PanelId>?> remainingPanelsOf) : ITurnGate
{
    private Dictionary<PanelId, IReadOnlyList<AgvId>> _order = [];

    /// <summary>計画を立て直したら、通る順番を入れ替える。</summary>
    public void Update(IReadOnlyDictionary<string, IReadOnlyList<string>> passingOrder)
    {
        ArgumentNullException.ThrowIfNull(passingOrder);
        _order = passingOrder.ToDictionary(
            pair => new PanelId(pair.Key),
            pair => (IReadOnlyList<AgvId>)pair.Value.Select(owner => new AgvId(owner)).ToArray());
    }

    public bool IsMyTurn(AgvId agv, IReadOnlyList<PanelId> panels) => panels.Distinct().All(panel => IsNext(agv, panel));

    private bool IsNext(AgvId agv, PanelId panel)
    {
        if (!_order.TryGetValue(panel, out var queue))
        {
            return true; // 計画にないパネル（順番の決まりがない）
        }

        // 通り終えた回数だけ、先頭から読み飛ばす
        var finished = queue
            .GroupBy(owner => owner)
            .ToDictionary(group => group.Key, group => Math.Max(0, group.Count() - RemainingVisits(group.Key, panel)));
        foreach (var owner in queue)
        {
            if (finished[owner] > 0)
            {
                finished[owner]--;
                continue;
            }

            return owner == agv;
        }

        return true;
    }

    /// <summary>その AGV が、これからそのパネルを使う回数。わからなければ、通り終えることがないものとする。</summary>
    private int RemainingVisits(AgvId owner, PanelId panel) =>
        remainingPanelsOf(owner) is { } panels ? panels.Count(p => p == panel) : int.MaxValue;
}
