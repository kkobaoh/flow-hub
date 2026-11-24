using FlowHub.Api.Database;
using FlowHub.Map;
using FlowHub.Map.Infrastructure;

namespace FlowHub.IntegrationTests;

/// <summary>
/// マップの DB を、ローカルの PostgreSQL で確かめる（マイグレーションを当て、保存して読み直す）。
/// </summary>
public class MapDatabaseTests
{
    private static readonly PanelId A = new("A");
    private static readonly PanelId B = new("B");
    private static readonly PanelId T = new("T");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly Outline UnderT = new(1500, -800, 2000, 1600);

    /// <summary>描く情報つきのマップを作る。パネルは並べた順に東へ1枚ずつ置き、受け渡しパネルには外形を置く。</summary>
    private static MapSnapshotResult Drawn(IEnumerable<Panel> panels, IEnumerable<Connection> connections)
    {
        var list = panels.ToList();
        var placements = list.Select((p, i) => KeyValuePair.Create(p.Id, new PanelPlacement(new GridPosition(i, 0), new CrossOffset(i * 10, -i * 5), p.Type == PanelType.Transfer ? UnderT : null)));
        return MapSnapshot.Create(list, connections, drawing: new MapDrawing(1000, new Dictionary<PanelId, PanelPlacement>(placements)));
    }

    private static Task<DbMapStore> OpenAsync(DatabaseOptions options) =>
        MapDatabase.OpenAsync(
            builder => Databases.Configure(builder, options, MapDatabase.Schema),
            (context, cancellationToken) => Databases.InitializeAsync(context, options, cancellationToken),
            Token);

    [Fact]
    public async Task マイグレーションを当ててマップの版と描く情報とキャリアの高さを保存し読み直せる()
    {
        var options = LocalPostgres.Options();
        await LocalPostgres.DropSchemaAsync(options, MapDatabase.Schema, Token);

        var store = await OpenAsync(options);
        store.Publish(Drawn(
            [new Panel(A, PanelType.Normal, false), new Panel(B, PanelType.Normal, false)],
            [new Connection(A, B, 800, new Heading(0), OneWay: false)]).Snapshot!);
        var latest = Drawn(
            [new Panel(A, PanelType.Normal, false), new Panel(T, PanelType.Transfer, true)],
            [new Connection(A, T, 750, new Heading(90), OneWay: false)]).Snapshot!;
        store.Publish(latest);

        // PostgreSQL の日時の列は UTC しか受け付けないので、UTC 以外の時刻でも保存できることを確かめる
        store.SetCarrierHeight(T, 120, new DateTimeOffset(2026, 11, 18, 18, 0, 0, TimeSpan.FromHours(9)));

        var reopened = await OpenAsync(options);

        Assert.Equal([1, 2], await reopened.GetPublishedVersionsAsync(Token));
        Assert.Equal(latest.Panels, reopened.Current!.Panels);
        Assert.Equal(latest.Connections, reopened.Current!.Connections);
        Assert.Equal(120, reopened.CarrierHeightOf(T));
        Assert.Equal(latest.Drawing!.Placements[T], reopened.Current!.Drawing!.Placements[T]);
        Assert.Equal(2, reopened.CurrentVersion);
    }
}
