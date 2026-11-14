using System.Collections.Immutable;
using FlowHub.Map.Domain;

namespace FlowHub.Map;

/// <summary>
/// 検証済みで、変更できないマップ。
/// </summary>
public sealed class MapSnapshot
{
    private MapSnapshot(ImmutableArray<Panel> panels, ImmutableArray<Connection> connections)
    {
        Panels = panels;
        Connections = connections;
    }

    public IReadOnlyList<Panel> Panels { get; }

    public IReadOnlyList<Connection> Connections { get; }

    /// <summary>
    /// パネルとつながりを検証し、違反がなければスナップショットを作る。違反はすべてまとめて返す。
    /// </summary>
    public static MapSnapshotResult Create(IEnumerable<Panel> panels, IEnumerable<Connection> connections)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(connections);

        var panelArray = panels.ToImmutableArray();
        var connectionArray = connections.ToImmutableArray();

        var errors = MapValidator.Validate(panelArray, connectionArray);
        return errors.Count == 0
            ? new MapSnapshotResult(new MapSnapshot(panelArray, connectionArray), errors)
            : new MapSnapshotResult(null, errors);
    }
}

/// <summary>
/// スナップショットを作った結果。違反がなければ <see cref="Snapshot"/> が入り、あれば <see cref="Errors"/> が入る。
/// </summary>
public sealed record MapSnapshotResult(MapSnapshot? Snapshot, IReadOnlyList<MapValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
