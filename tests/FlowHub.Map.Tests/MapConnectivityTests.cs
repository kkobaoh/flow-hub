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
        var result = MapSnapshot.Create(
            [Normal("A"), Normal("B")],
            [Connect("A", "B", oneWay: true)],
            RequireConnected);

        Assert.True(result.IsValid);
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
