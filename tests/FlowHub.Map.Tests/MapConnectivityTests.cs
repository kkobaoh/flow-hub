namespace FlowHub.Map.Tests;

public class MapConnectivityTests
{
    private static Panel Normal(string id) => new(new PanelId(id), PanelType.Normal, HasChargingPin: false);

    private static Connection Connect(string from, string to, bool oneWay = false) =>
        new(new PanelId(from), new PanelId(to), 800, new Heading(0), oneWay);

    private static readonly MapValidationOptions RequireConnected = new() { RequireConnected = true };

    // A ─ B    C ─ D  （2つの離れ島）
    private static readonly Panel[] TwoIslands = [Normal("A"), Normal("B"), Normal("C"), Normal("D")];
    private static readonly Connection[] TwoIslandConnections = [Connect("A", "B"), Connect("C", "D")];

    [Fact]
    public void 既定では離れ島を許す()
    {
        var result = MapSnapshot.Create(TwoIslands, TwoIslandConnections);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void オプションを指定すると離れ島を違反にする()
    {
        var result = MapSnapshot.Create(TwoIslands, TwoIslandConnections, RequireConnected);

        var error = Assert.Single(result.Errors);
        Assert.Equal(MapValidationErrorCode.NotConnected, error.Code);
    }

    [Fact]
    public void 離れ島の違反のメッセージにはそれぞれの島のパネルが入る()
    {
        var result = MapSnapshot.Create(TwoIslands, TwoIslandConnections, RequireConnected);

        var message = result.Errors.Single().Message;
        Assert.Contains("A、B", message, StringComparison.Ordinal);
        Assert.Contains("C、D", message, StringComparison.Ordinal);
    }

    [Fact]
    public void オプションを指定してもすべてつながっていれば違反にならない()
    {
        var result = MapSnapshot.Create(
            [Normal("A"), Normal("B"), Normal("C")],
            [Connect("A", "B"), Connect("B", "C")],
            RequireConnected);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void 一方通行でもつながっていれば1つの島とみなす()
    {
        // A → B → C → A の一方通行の輪
        var result = MapSnapshot.Create(
            [Normal("A"), Normal("B"), Normal("C")],
            [Connect("A", "B", oneWay: true), Connect("B", "C", oneWay: true), Connect("C", "A", oneWay: true)],
            RequireConnected);

        Assert.True(result.IsValid);
    }

    // ---- 島の中ではどこへでも行ける（常に検証する） ----

    [Fact]
    public void 一方通行で入れるのに出られない区画は違反()
    {
        // A → B → C（一方通行）。C から A へ戻れない
        var result = MapSnapshot.Create(
            [Normal("A"), Normal("B"), Normal("C")],
            [Connect("A", "B", oneWay: true), Connect("B", "C", oneWay: true)]);

        var error = Assert.Single(result.Errors);
        Assert.Equal(MapValidationErrorCode.NotStronglyConnected, error.Code);
    }

    [Fact]
    public void 出られない区画の違反のメッセージには互いに行き来できるパネルのまとまりが入る()
    {
        // A ⇄ B → C ⇄ D（B → C だけが一方通行）
        var result = MapSnapshot.Create(
            [Normal("A"), Normal("B"), Normal("C"), Normal("D")],
            [Connect("A", "B"), Connect("B", "C", oneWay: true), Connect("C", "D")]);

        var message = result.Errors.Single().Message;
        Assert.Contains("A、B", message, StringComparison.Ordinal);
        Assert.Contains("C、D", message, StringComparison.Ordinal);
    }

    [Fact]
    public void 一方通行の輪になっていればどこへでも行けるので違反にならない()
    {
        var result = MapSnapshot.Create(
            [Normal("A"), Normal("B"), Normal("C")],
            [Connect("A", "B", oneWay: true), Connect("B", "C", oneWay: true), Connect("C", "A", oneWay: true)]);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void 離れ島はそれぞれの島の中でどこへでも行ければ違反にならない()
    {
        // 離れ島はオプションを指定しなければ許す。島の中の検証は常に行う
        var result = MapSnapshot.Create(TwoIslands, TwoIslandConnections);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void 離れ島の1つに出られない区画があれば違反()
    {
        // A ⇄ B と、C → D（一方通行）
        var result = MapSnapshot.Create(
            [Normal("A"), Normal("B"), Normal("C"), Normal("D")],
            [Connect("A", "B"), Connect("C", "D", oneWay: true)]);

        Assert.Equal([MapValidationErrorCode.NotStronglyConnected], result.Errors.Select(e => e.Code));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void パネルが1枚以下なら離れ島はない(int count)
    {
        var panels = Enumerable.Range(0, count).Select(i => Normal($"P{i}"));

        var result = MapSnapshot.Create(panels, [], RequireConnected);

        Assert.True(result.IsValid);
    }
}
