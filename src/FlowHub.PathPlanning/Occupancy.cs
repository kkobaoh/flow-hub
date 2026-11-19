namespace FlowHub.PathPlanning;

/// <summary>
/// ノードを使う時間帯（tick。[Start, End)）。End が <see cref="long.MaxValue"/> なら、ずっと使う。
/// </summary>
/// <param name="Provisional">まだ計画していない AGV の、仮の「ずっと塞いでいる」。計画したら外す。</param>
internal sealed record Reservation(string Owner, long Start, long End, bool Provisional);

/// <summary>
/// ノードごとの、使われている時間帯。そこから、空いている時間帯を求める。
/// </summary>
internal sealed class Occupancy
{
    public const long Forever = long.MaxValue;

    private readonly Dictionary<string, List<Reservation>> _reservations = [];

    public void Reserve(string node, Reservation reservation)
    {
        if (!_reservations.TryGetValue(node, out var list))
        {
            list = [];
            _reservations[node] = list;
        }

        list.Add(reservation);
    }

    /// <summary>その AGV の、仮の「ずっと塞いでいる」を外す。</summary>
    public void RemoveProvisional(string owner)
    {
        foreach (var list in _reservations.Values)
        {
            list.RemoveAll(r => r.Provisional && r.Owner == owner);
        }
    }

    /// <summary>
    /// ノードの空いている時間帯（開始の順）。仮の予約を無視するときは、仮の予約がないものとして求める。
    /// </summary>
    public IReadOnlyList<(long Start, long End)> FreeIntervals(string node, bool ignoreProvisional)
    {
        var busy = _reservations.TryGetValue(node, out var list)
            ? list.Where(r => !(ignoreProvisional && r.Provisional)).OrderBy(r => r.Start).ToList()
            : [];

        var free = new List<(long, long)>();
        var cursor = 0L;
        foreach (var reservation in busy)
        {
            if (reservation.Start > cursor)
            {
                free.Add((cursor, reservation.Start));
            }

            cursor = Math.Max(cursor, reservation.End);
            if (cursor == Forever)
            {
                return free;
            }
        }

        free.Add((cursor, Forever));
        return free;
    }

    /// <summary>そのノードを、どの AGV も（仮にも）使わないか。</summary>
    public bool IsUnused(string node) => !_reservations.TryGetValue(node, out var list) || list.Count == 0;

    /// <summary>仮の予約を持っている AGV のうち、そのノードを塞いでいるもの。</summary>
    public IEnumerable<string> ProvisionalOwnersAt(string node) =>
        _reservations.TryGetValue(node, out var list) ? list.Where(r => r.Provisional).Select(r => r.Owner) : [];

    /// <summary>ノードごとの、使う AGV（使い始めの順。同じ時刻なら、予約した順）。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> PassingOrder() =>
        _reservations
            .Where(pair => pair.Value.Any(r => !r.Provisional))
            .ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value
                    .Where(r => !r.Provisional)
                    .Select((r, index) => (r, index))
                    .OrderBy(x => x.r.Start)
                    .ThenBy(x => x.index)
                    .Select(x => x.r.Owner)
                    .ToArray());
}
