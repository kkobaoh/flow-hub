using FlowHub.Map;

namespace FlowHub.Traffic.Domain;

/// <summary>
/// パネルの予約の結果。
/// </summary>
/// <param name="Conflicts">ほかの AGV が予約していて、予約できなかったパネル。予約できたときは空。</param>
/// <param name="BlockingAgvs">予約できなかったパネルを予約している AGV。</param>
internal sealed record ReservationResult(IReadOnlyList<PanelId> Conflicts, IReadOnlyList<AgvId> BlockingAgvs)
{
    public bool Succeeded => Conflicts.Count == 0;
}

/// <summary>
/// パネルの予約表。予約したパネルには、ほかの AGV は入れない。
/// 安全（AGV 同士の衝突の防止）は、この予約で保証する（ルーティングには頼らない）。
/// </summary>
internal sealed class PanelReservations
{
    private readonly Dictionary<PanelId, AgvId> _owners = [];

    /// <summary>
    /// パネルをまとめて予約する。1枚でもほかの AGV が予約していたら、何も予約しない。
    /// 自分がすでに予約しているパネルは、重ねて予約できる。
    /// </summary>
    public ReservationResult TryReserve(AgvId agv, IEnumerable<PanelId> panels)
    {
        ArgumentNullException.ThrowIfNull(panels);

        var requested = panels.Distinct().ToArray();
        var conflicts = requested
            .Where(panel => _owners.TryGetValue(panel, out var owner) && owner != agv)
            .ToArray();

        if (conflicts.Length > 0)
        {
            var blocking = conflicts.Select(panel => _owners[panel]).Distinct().ToArray();
            return new ReservationResult(conflicts, blocking);
        }

        foreach (var panel in requested)
        {
            _owners[panel] = agv;
        }

        return new ReservationResult([], []);
    }

    /// <summary>
    /// 自分が予約しているパネルを解放する。予約されていないパネルなら何もしない。
    /// </summary>
    /// <exception cref="InvalidOperationException">ほかの AGV が予約しているパネルを解放しようとしたとき。</exception>
    public void Release(AgvId agv, PanelId panel)
    {
        if (!_owners.TryGetValue(panel, out var owner))
        {
            return;
        }

        if (owner != agv)
        {
            throw new InvalidOperationException($"パネル {panel} は {owner} が予約しているため、{agv} からは解放できません。");
        }

        _owners.Remove(panel);
    }

    /// <summary>
    /// AGV が予約しているパネルをすべて解放する。
    /// </summary>
    public void ReleaseAll(AgvId agv)
    {
        foreach (var panel in ReservedBy(agv))
        {
            _owners.Remove(panel);
        }
    }

    /// <summary>
    /// パネルを予約している AGV。予約されていなければ null。
    /// </summary>
    public AgvId? OwnerOf(PanelId panel) => _owners.TryGetValue(panel, out var owner) ? owner : null;

    /// <summary>
    /// AGV が予約しているパネル。
    /// </summary>
    public IReadOnlyList<PanelId> ReservedBy(AgvId agv) =>
        _owners.Where(pair => pair.Value == agv).Select(pair => pair.Key).ToArray();
}
