using System.Collections.Immutable;
using FlowHub.Map.Domain;

namespace FlowHub.Map;

/// <summary>
/// 検証済みで、変更できないマップ。
/// </summary>
public sealed class MapSnapshot
{
    private MapSnapshot(ImmutableArray<Panel> panels, ImmutableArray<Connection> connections, MapDrawing? drawing)
    {
        Panels = panels;
        Connections = connections;
        Drawing = drawing;
    }

    public IReadOnlyList<Panel> Panels { get; }

    public IReadOnlyList<Connection> Connections { get; }

    /// <summary>画面に描くための情報（パネルの位置、外形）。DB に残す版は必ず持つ。走行の判断には使わない。</summary>
    public MapDrawing? Drawing { get; }

    /// <summary>
    /// パネルとつながり（と、描く情報があればそれも）を検証し、違反がなければスナップショットを作る。違反はすべてまとめて返す。
    /// </summary>
    public static MapSnapshotResult Create(
        IEnumerable<Panel> panels,
        IEnumerable<Connection> connections,
        MapValidationOptions? options = null,
        MapDrawing? drawing = null)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(connections);

        var panelArray = panels.ToImmutableArray();
        var connectionArray = connections.ToImmutableArray();

        var errors = MapValidator.Validate(panelArray, connectionArray, drawing, options ?? MapValidationOptions.Default);
        return errors.Count == 0
            ? new MapSnapshotResult(new MapSnapshot(panelArray, connectionArray, drawing), errors)
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
