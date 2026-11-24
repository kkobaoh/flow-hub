namespace FlowHub.Map.Tests;

public class InMemoryMapStoreTests
{
    private static readonly PanelId A = new("A");
    private static readonly PanelId T = new("T");
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    private static MapSnapshot Map() =>
        MapSnapshot.Create(
            [new Panel(A, PanelType.Normal, false), new Panel(T, PanelType.Transfer, true)],
            [new Connection(A, T, 800, new Heading(0), OneWay: false)]).Snapshot!;

    [Fact]
    public void 公開したマップが今の版になる()
    {
        var store = new InMemoryMapStore();
        var map = Map();

        store.Publish(map);

        Assert.Same(map, store.Current);
    }

    [Fact]
    public void マップを公開する前は今の版がない()
    {
        Assert.Null(new InMemoryMapStore().Current);
    }

    [Fact]
    public void 今の版の番号は公開するたびに1つ進む()
    {
        var store = new InMemoryMapStore();
        Assert.Equal(0, store.CurrentVersion);

        store.Publish(Map());
        store.Publish(Map());

        Assert.Equal(2, store.CurrentVersion);
    }

    [Fact]
    public void キャリアの高さは設定されるまでない()
    {
        var store = new InMemoryMapStore();
        store.Publish(Map());

        Assert.Null(store.CarrierHeightOf(T));
    }

    [Fact]
    public void キャリアの高さを設定できる()
    {
        var store = new InMemoryMapStore();
        store.Publish(Map());

        store.SetCarrierHeight(T, heightMm: 120, setAt: Now);

        Assert.Equal(120, store.CarrierHeightOf(T));
    }

    [Fact]
    public void キャリアの高さは新しく設定されたほうを使う()
    {
        var store = new InMemoryMapStore();
        store.Publish(Map());
        store.SetCarrierHeight(T, heightMm: 120, setAt: Now);

        store.SetCarrierHeight(T, heightMm: 100, setAt: Now.AddMinutes(-1)); // 古い設定（遅れて届いた測定など）
        Assert.Equal(120, store.CarrierHeightOf(T));

        store.SetCarrierHeight(T, heightMm: 118, setAt: Now.AddMinutes(1));
        Assert.Equal(118, store.CarrierHeightOf(T));
    }

    [Fact]
    public void 受け渡しパネルでないパネルにはキャリアの高さを設定できない()
    {
        var store = new InMemoryMapStore();
        store.Publish(Map());

        Assert.Throws<ArgumentException>(() => store.SetCarrierHeight(A, heightMm: 120, setAt: Now));
    }

    [Fact]
    public void キャリアの高さは正の値でなければならない()
    {
        var store = new InMemoryMapStore();
        store.Publish(Map());

        Assert.Throws<ArgumentOutOfRangeException>(() => store.SetCarrierHeight(T, heightMm: 0, setAt: Now));
    }

    [Fact]
    public void マップを公開する前はキャリアの高さを設定できない()
    {
        Assert.Throws<InvalidOperationException>(() => new InMemoryMapStore().SetCarrierHeight(T, 120, Now));
    }
}
