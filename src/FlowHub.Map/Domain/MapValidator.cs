namespace FlowHub.Map.Domain;

/// <summary>
/// マップの検証。違反をすべて集めて返す。
/// </summary>
internal static class MapValidator
{
    public static IReadOnlyList<MapValidationError> Validate(
        IReadOnlyList<Panel> panels,
        IReadOnlyList<Connection> connections,
        MapValidationOptions options)
    {
        var errors = new List<MapValidationError>();

        var panelsById = ValidatePanels(panels, errors);
        var validConnections = ValidateConnections(connections, panelsById, errors);
        ValidateDeadEnds(panelsById, validConnections, errors);

        if (options.RequireConnected)
        {
            ValidateConnected(panelsById, validConnections, errors);
        }

        return errors;
    }

    /// <summary>
    /// すべてのパネルが1つにつながっているかを検証する。つながりの向き（一方通行）は問わない。
    /// </summary>
    private static void ValidateConnected(
        Dictionary<PanelId, Panel> panelsById,
        List<Connection> connections,
        List<MapValidationError> errors)
    {
        var islands = FindIslands(panelsById.Keys, connections);
        if (islands.Count <= 1)
        {
            return;
        }

        var description = string.Join(" / ", islands.Select(island => $"{{{string.Join("、", island)}}}"));
        errors.Add(new(
            MapValidationErrorCode.NotConnected,
            $"マップが {islands.Count} つの離れ島に分かれています：{description}"));
    }

    private static List<List<PanelId>> FindIslands(IEnumerable<PanelId> panelIds, List<Connection> connections)
    {
        var neighbors = panelIds.ToDictionary(id => id, _ => new List<PanelId>());
        foreach (var connection in connections)
        {
            neighbors[connection.From].Add(connection.To);
            neighbors[connection.To].Add(connection.From);
        }

        var islands = new List<List<PanelId>>();
        var visited = new HashSet<PanelId>();

        foreach (var start in neighbors.Keys)
        {
            if (!visited.Add(start))
            {
                continue;
            }

            var island = new List<PanelId>();
            var pending = new Queue<PanelId>([start]);
            while (pending.TryDequeue(out var current))
            {
                island.Add(current);
                foreach (var next in neighbors[current].Where(visited.Add))
                {
                    pending.Enqueue(next);
                }
            }

            islands.Add(island);
        }

        return islands;
    }

    private static Dictionary<PanelId, Panel> ValidatePanels(IReadOnlyList<Panel> panels, List<MapValidationError> errors)
    {
        var panelsById = new Dictionary<PanelId, Panel>();

        foreach (var panel in panels)
        {
            if (!panelsById.TryAdd(panel.Id, panel))
            {
                errors.Add(new(MapValidationErrorCode.DuplicatePanelId, $"パネル {panel.Id} の ID が重複しています。"));
            }

            if (panel.HasChargingPin && panel.Type == PanelType.Normal)
            {
                errors.Add(new(MapValidationErrorCode.ChargingPinOnNormalPanel, $"通常パネル {panel.Id} には充電ピンを設置できません。"));
            }
        }

        return panelsById;
    }

    /// <summary>
    /// つながりを検証し、パネルの組として正しいつながり（存在するパネルどうしで、自分自身でも重複でもないもの）を返す。
    /// </summary>
    private static List<Connection> ValidateConnections(
        IReadOnlyList<Connection> connections,
        Dictionary<PanelId, Panel> panelsById,
        List<MapValidationError> errors)
    {
        var validConnections = new List<Connection>();
        var connectedPairs = new HashSet<(PanelId, PanelId)>();

        foreach (var connection in connections)
        {
            if (connection.DistanceMm <= 0)
            {
                errors.Add(new(MapValidationErrorCode.NonPositiveDistance, $"{Describe(connection)} の距離は正の値にしてください。"));
            }

            var unknown = new[] { connection.From, connection.To }.Where(id => !panelsById.ContainsKey(id)).Distinct().ToArray();
            if (unknown.Length > 0)
            {
                errors.Add(new(MapValidationErrorCode.UnknownPanel, $"{Describe(connection)} のパネル {string.Join("、", unknown)} が存在しません。"));
                continue;
            }

            if (connection.From == connection.To)
            {
                errors.Add(new(MapValidationErrorCode.SelfConnection, $"パネル {connection.From} を自分自身につなげることはできません。"));
                continue;
            }

            if (!connectedPairs.Add(UnorderedPair(connection.From, connection.To)))
            {
                errors.Add(new(MapValidationErrorCode.DuplicateConnection, $"{Describe(connection)} が二重につながっています。"));
                continue;
            }

            validConnections.Add(connection);
        }

        return validConnections;
    }

    private static void ValidateDeadEnds(
        Dictionary<PanelId, Panel> panelsById,
        List<Connection> connections,
        List<MapValidationError> errors)
    {
        foreach (var deadEnd in panelsById.Values.Where(p => p.IsDeadEnd))
        {
            var own = connections.Where(c => c.From == deadEnd.Id || c.To == deadEnd.Id).ToArray();

            if (own.Length != 1)
            {
                errors.Add(new(
                    MapValidationErrorCode.DeadEndMustHaveSingleConnection,
                    $"行き止まりのパネル {deadEnd.Id} のつながりは1つにしてください（今は {own.Length} つです）。"));
            }

            foreach (var connection in own)
            {
                var neighbor = panelsById[connection.From == deadEnd.Id ? connection.To : connection.From];
                if (neighbor.Type != PanelType.Normal)
                {
                    errors.Add(new(
                        MapValidationErrorCode.DeadEndMustConnectToNormalPanel,
                        $"行き止まりのパネル {deadEnd.Id} の隣（{neighbor.Id}）は通常パネルにしてください。"));
                }

                if (connection.OneWay)
                {
                    errors.Add(new(
                        MapValidationErrorCode.OneWayToDeadEnd,
                        $"行き止まりのパネル {deadEnd.Id} へのつながりは、一方通行にできません（前進で入り、後退で出るため）。"));
                }
            }
        }
    }

    private static (PanelId, PanelId) UnorderedPair(PanelId a, PanelId b) =>
        string.CompareOrdinal(a.Value, b.Value) <= 0 ? (a, b) : (b, a);

    private static string Describe(Connection connection) => $"パネル {connection.From} と {connection.To} のつながり";
}
