namespace FlowHub.Routing;

/// <summary>
/// 経路探索の入力にするノード（マップのパネルにあたる）。
/// </summary>
/// <param name="Id">ノードの ID。</param>
/// <param name="CanRotate">その場で回転できるか。</param>
/// <param name="ForwardEntryOnly">前進でしか入れないか。</param>
/// <param name="BackwardExitOnly">後退でしか出られないか。</param>
public sealed record RouteNode(string Id, bool CanRotate, bool ForwardEntryOnly, bool BackwardExitOnly);

/// <summary>
/// 経路探索の入力にする辺（隣り合うパネルのつながりにあたる）。
/// </summary>
public sealed record RouteEdge
{
    /// <param name="From">辺の一方の端のノード。</param>
    /// <param name="To">辺のもう一方の端のノード。</param>
    /// <param name="DistanceMm">距離（mm）。正の値。</param>
    /// <param name="Heading">From から To へ進むときの向き（絶対角度、0度以上360度未満）。</param>
    /// <param name="OneWay">一方通行か。true のときは From から To へだけ進める。</param>
    public RouteEdge(string From, string To, int DistanceMm, int Heading, bool OneWay)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(DistanceMm);
        Angles.ThrowIfOutOfRange(Heading);

        this.From = From;
        this.To = To;
        this.DistanceMm = DistanceMm;
        this.Heading = Heading;
        this.OneWay = OneWay;
    }

    public string From { get; }

    public string To { get; }

    public int DistanceMm { get; }

    public int Heading { get; }

    public bool OneWay { get; }
}

/// <summary>
/// 経路探索の入力にするグラフ。マップのスナップショットからこの形に変換するのは、呼び出す側の役割。
/// 避けたいノードがあるときは、呼び出す側がグラフから取り除いてから渡す。
/// </summary>
public sealed class RouteGraph
{
    private readonly Dictionary<string, RouteNode> _nodes;
    private readonly Dictionary<string, List<Traversal>> _traversals;
    private readonly Dictionary<string, HashSet<int>> _headings;

    public RouteGraph(IEnumerable<RouteNode> nodes, IEnumerable<RouteEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        _nodes = [];
        foreach (var node in nodes)
        {
            if (!_nodes.TryAdd(node.Id, node))
            {
                throw new ArgumentException($"ノード {node.Id} が重複しています。", nameof(nodes));
            }
        }

        _traversals = _nodes.Keys.ToDictionary(id => id, _ => new List<Traversal>());
        _headings = _nodes.Keys.ToDictionary(id => id, _ => new HashSet<int>());

        foreach (var edge in edges)
        {
            if (!_nodes.ContainsKey(edge.From) || !_nodes.ContainsKey(edge.To))
            {
                throw new ArgumentException($"辺 {edge.From}-{edge.To} が存在しないノードを指しています。", nameof(edges));
            }

            var reverse = Angles.Reverse(edge.Heading);

            _traversals[edge.From].Add(new Traversal(edge.To, edge.Heading, edge.DistanceMm));
            if (!edge.OneWay)
            {
                _traversals[edge.To].Add(new Traversal(edge.From, reverse, edge.DistanceMm));
            }

            // その場で回転するときの向きの候補：辺の向きと、その逆向き（後退で進むとき）
            _headings[edge.From].UnionWith([edge.Heading, reverse]);
            _headings[edge.To].UnionWith([edge.Heading, reverse]);
        }
    }

    internal bool Contains(string nodeId) => _nodes.ContainsKey(nodeId);

    internal RouteNode Node(string nodeId) => _nodes[nodeId];

    /// <summary>
    /// ノードから進める先。<see cref="Traversal.Heading"/> は進む方向の絶対角度。
    /// </summary>
    internal IReadOnlyList<Traversal> TraversalsFrom(string nodeId) => _traversals[nodeId];

    /// <summary>
    /// ノードで回転するときに向く候補の角度。
    /// </summary>
    internal IReadOnlyCollection<int> HeadingsAt(string nodeId) => _headings[nodeId];

    internal readonly record struct Traversal(string To, int Heading, int DistanceMm);
}
