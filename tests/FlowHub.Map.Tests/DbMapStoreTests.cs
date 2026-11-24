using FlowHub.Map.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FlowHub.Map.Tests;

/// <summary>
/// マップを DB に保存する置き場のテスト。メモリ上の SQLite を使う（同じ接続を開いている間だけ中身が残る）。
/// </summary>
public sealed class DbMapStoreTests : IDisposable
{
    private static readonly PanelId A = new("A");
    private static readonly PanelId B = new("B");
    private static readonly PanelId T = new("T");
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public DbMapStoreTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// マップ置き場を開く。同じ接続を使うので、開き直すと「次に起動したとき」と同じになる。
    /// </summary>
    private Task<DbMapStore> OpenAsync() =>
        MapDatabase.OpenAsync(
            builder => builder.UseSqlite(_connection),
            (context, cancellationToken) => context.Database.EnsureCreatedAsync(cancellationToken),
            Token);

    /// <summary>受け渡しパネルの下にあるものの外形（見本）。</summary>
    private static readonly Outline UnderT = new(1500, -800, 2000, 1600);

    /// <summary>
    /// 描く情報つきのマップを作る（DB に残す版は、描く情報を必ず持つため）。パネルは並べた順に東へ1枚ずつ置き、受け渡しパネルには外形を置く。
    /// </summary>
    private static MapSnapshotResult Drawn(IEnumerable<Panel> panels, IEnumerable<Connection> connections)
    {
        var list = panels.ToList();
        var placements = list.Select((p, i) => KeyValuePair.Create(p.Id, new PanelPlacement(new GridPosition(i, 0), CrossOffset.None, p.Type == PanelType.Transfer ? UnderT : null)));
        return MapSnapshot.Create(list, connections, drawing: new MapDrawing(1000, new Dictionary<PanelId, PanelPlacement>(placements)));
    }

    /// <summary>A →(0度) B →(90度) T（受け渡しパネル、充電ピンあり）。</summary>
    private static MapSnapshot ThreePanels() =>
        Drawn(
            [
                new Panel(A, PanelType.Normal, false),
                new Panel(B, PanelType.Normal, false),
                new Panel(T, PanelType.Transfer, true),
            ],
            [
                new Connection(A, B, 800, new Heading(0), OneWay: false),
                new Connection(B, T, 750, new Heading(90), OneWay: false),
            ]).Snapshot!;

    /// <summary>A →(0度、900mm) B。</summary>
    private static MapSnapshot TwoPanels() =>
        Drawn(
            [new Panel(A, PanelType.Normal, false), new Panel(B, PanelType.Normal, false)],
            [new Connection(A, B, 900, new Heading(0), OneWay: false)]).Snapshot!;

    // ---- マップの版 ----

    [Fact]
    public async Task まだ公開していなければ今の版はない()
    {
        var store = await OpenAsync();

        Assert.Null(store.Current);
    }

    [Fact]
    public async Task 公開したマップは次に開いたときも同じ内容で読める()
    {
        var store = await OpenAsync();
        store.Publish(ThreePanels());

        var reopened = await OpenAsync();

        var map = reopened.Current!;
        Assert.Equal(ThreePanels().Panels, map.Panels);
        Assert.Equal(ThreePanels().Connections, map.Connections);
    }

    [Fact]
    public async Task 描く情報も次に開いたときに読める()
    {
        var store = await OpenAsync();
        store.Publish(ThreePanels());

        var reopened = await OpenAsync();

        var drawing = reopened.Current!.Drawing!;
        Assert.Equal(1000, drawing.PanelSizeMm);
        Assert.Equal(new PanelPlacement(new GridPosition(2, 0), CrossOffset.None, UnderT), drawing.Placements[T]);
        Assert.Equal(new PanelPlacement(new GridPosition(0, 0), CrossOffset.None, null), drawing.Placements[A]);
    }

    [Fact]
    public async Task 描く情報のないマップはDBに残せない()
    {
        var store = await OpenAsync();
        var undrawn = MapSnapshot.Create([new Panel(A, PanelType.Normal, false)], []).Snapshot!;

        Assert.Throws<ArgumentException>(() => store.Publish(undrawn));
        Assert.Null(store.Current);
    }

    [Fact]
    public async Task 今の版の番号は公開するたびに進み開き直しても続く()
    {
        var store = await OpenAsync();
        Assert.Equal(0, store.CurrentVersion);
        store.Publish(ThreePanels());
        store.Publish(TwoPanels());

        var reopened = await OpenAsync();

        Assert.Equal(2, reopened.CurrentVersion);
    }

    [Fact]
    public async Task 特殊消耗品受け渡しパネルとキャリアを下ろす角度も次に開いたときに読める()
    {
        var store = await OpenAsync();
        var special = new Panel(new PanelId("X"), PanelType.SpecialTransfer, false) { CarrierLoweringHeading = new Heading(215) };
        store.Publish(Drawn(
            [new Panel(A, PanelType.Normal, false), special],
            [new Connection(A, special.Id, 800, new Heading(0), OneWay: false)]).Snapshot!);

        var reopened = await OpenAsync();

        Assert.Equal(special, reopened.Current!.Panels[1]);
    }

    [Fact]
    public async Task 公開した版はすべて番号を付けて残し次に開いたときは一番新しい版を使う()
    {
        var store = await OpenAsync();
        store.Publish(ThreePanels());
        store.Publish(TwoPanels());

        var reopened = await OpenAsync();

        Assert.Equal([1, 2], await reopened.GetPublishedVersionsAsync(Token));
        Assert.Equal(TwoPanels().Connections, reopened.Current!.Connections);
    }

    // ---- キャリアの高さ ----

    [Fact]
    public async Task キャリアの高さは次に開いたときも読める()
    {
        var store = await OpenAsync();
        store.Publish(ThreePanels());
        store.SetCarrierHeight(T, 120, T0);

        var reopened = await OpenAsync();

        Assert.Equal(120, reopened.CarrierHeightOf(T));
    }

    [Fact]
    public async Task キャリアの高さは新しく設定されたほうを残す()
    {
        var store = await OpenAsync();
        store.Publish(ThreePanels());
        store.SetCarrierHeight(T, 120, T0.AddMinutes(1));
        store.SetCarrierHeight(T, 100, T0); // 古い設定

        var reopened = await OpenAsync();

        Assert.Equal(120, reopened.CarrierHeightOf(T));
    }

    [Fact]
    public async Task キャリアの高さを書き換えたら新しい値が残る()
    {
        var store = await OpenAsync();
        store.Publish(ThreePanels());
        store.SetCarrierHeight(T, 120, T0);
        store.SetCarrierHeight(T, 135, T0.AddMinutes(1));

        var reopened = await OpenAsync();

        Assert.Equal(135, reopened.CarrierHeightOf(T));
    }

    [Fact]
    public async Task 受け渡しパネルでなければキャリアの高さを設定できず保存もしない()
    {
        var store = await OpenAsync();
        store.Publish(ThreePanels());

        Assert.Throws<ArgumentException>(() => store.SetCarrierHeight(A, 120, T0));

        var reopened = await OpenAsync();
        Assert.Null(reopened.CarrierHeightOf(A));
    }
}
