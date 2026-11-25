using FlowHub.Map.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FlowHub.Map.Tests;

/// <summary>
/// マップの下書き（サーバーに1つだけ。保存するたびに番号を進め、古い番号からの保存は断る）のテスト。メモリ上の SQLite を使う。
/// </summary>
public sealed class MapDraftStoreTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public MapDraftStoreTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>同じ接続を使うので、開き直すと「次に起動したとき」と同じになる。</summary>
    private Task<DbMapStore> OpenAsync() =>
        MapDatabase.OpenAsync(
            builder => builder.UseSqlite(_connection),
            (context, cancellationToken) => context.Database.EnsureCreatedAsync(cancellationToken),
            Token);

    /// <summary>検証していない下書き（つながりの先のパネルがないなど、公開できない形でも保存できる）。</summary>
    private static MapDraft Draft(string panelId = "P1") => new(
        PanelSizeMm: 1000,
        Panels:
        [
            new DraftPanel(panelId, "Normal", HasChargingPin: false, CarrierLoweringHeading: null, Column: 0, Row: 0, CrossOffsetEastMm: 0, CrossOffsetSouthMm: 0, Outline: null),
            new DraftPanel("D1", "Transfer", HasChargingPin: true, CarrierLoweringHeading: 90, Column: 1, Row: 0, CrossOffsetEastMm: 5, CrossOffsetSouthMm: -3, Outline: new DraftOutline(-500, -1200, 2000, 1500)),
        ],
        Connections: [new DraftConnection(panelId, "NOWHERE", DistanceMm: 1000, Heading: 90, OneWay: false)]);

    [Fact]
    public async Task 下書きがなければ番号0で中身はない()
    {
        var store = await OpenAsync();

        var draft = store.ReadDraft();

        Assert.Equal(0, draft.Revision);
        Assert.Null(draft.Saved);
    }

    [Fact]
    public async Task 保存した下書きは番号が進み次に開いたときも同じ中身で読める()
    {
        var store = await OpenAsync();

        var result = store.SaveDraft(expectedRevision: 0, Draft(), T0);

        Assert.Equal(new MapDraftSave(MapDraftSaveResult.Saved, 1), result);
        var reread = (await OpenAsync()).ReadDraft();
        Assert.Equal(1, reread.Revision);
        Assert.NotNull(reread.Saved);
        Assert.Equal(T0, reread.Saved.SavedAt);
        Assert.True(Draft().SameAs(reread.Saved.Draft));
    }

    [Fact]
    public async Task 読んだあとにほかの人が保存していたら上書きせずに断る()
    {
        var store = await OpenAsync();
        store.SaveDraft(0, Draft("P1"), T0);
        store.SaveDraft(1, Draft("P2"), T0.AddMinutes(1));

        var stale = store.SaveDraft(1, Draft("P3"), T0.AddMinutes(2));

        Assert.Equal(new MapDraftSave(MapDraftSaveResult.Stale, 2), stale);
        Assert.Equal("P2", store.ReadDraft().Saved!.Draft.Panels[0].Id);
    }

    [Fact]
    public async Task 下書きを消すと中身はなくなるが番号は進み消す前の番号からの保存は断る()
    {
        var store = await OpenAsync();
        store.SaveDraft(0, Draft(), T0);

        store.DeleteDraft();

        var after = (await OpenAsync()).ReadDraft();
        Assert.Equal(2, after.Revision);
        Assert.Null(after.Saved);
        Assert.Equal(MapDraftSaveResult.Stale, store.SaveDraft(1, Draft(), T0).Result);
        Assert.Equal(MapDraftSaveResult.Saved, store.SaveDraft(2, Draft(), T0).Result);
    }

    [Fact]
    public void 公開中の版から下書きを作る()
    {
        var panels = new[]
        {
            new Panel(new PanelId("A"), PanelType.Normal, HasChargingPin: false),
            new Panel(new PanelId("T"), PanelType.Transfer, HasChargingPin: true),
        };
        var connections = new[] { new Connection(new PanelId("A"), new PanelId("T"), 1000, new Heading(90), OneWay: false) };
        var drawing = new MapDrawing(1000, new Dictionary<PanelId, PanelPlacement>
        {
            [new PanelId("A")] = new(new GridPosition(0, 0), new CrossOffset(0, 0), null),
            [new PanelId("T")] = new(new GridPosition(1, 0), new CrossOffset(10, -20), new Outline(-500, -1200, 2000, 1500)),
        });
        var snapshot = MapSnapshot.Create(panels, connections, drawing: drawing).Snapshot!;

        var draft = MapDraft.FromSnapshot(snapshot);

        var expected = new MapDraft(
            1000,
            [
                new DraftPanel("A", "Normal", false, null, 0, 0, 0, 0, null),
                new DraftPanel("T", "Transfer", true, null, 1, 0, 10, -20, new DraftOutline(-500, -1200, 2000, 1500)),
            ],
            [new DraftConnection("A", "T", 1000, 90, false)]);
        Assert.True(expected.SameAs(draft));
    }
}
