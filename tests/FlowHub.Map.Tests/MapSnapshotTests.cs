namespace FlowHub.Map.Tests;

public class MapSnapshotTests
{
    private static Panel Normal(string id) => new(new PanelId(id), PanelType.Normal, HasChargingPin: false);

    private static Panel Transfer(string id, bool chargingPin = false) =>
        new(new PanelId(id), PanelType.Transfer, chargingPin);

    private static Panel Standby(string id, bool chargingPin = false) =>
        new(new PanelId(id), PanelType.Standby, chargingPin);

    private static Connection Connect(string from, string to, int distanceMm = 800, int heading = 0, bool oneWay = false) =>
        new(new PanelId(from), new PanelId(to), distanceMm, new Heading(heading), oneWay);

    private static MapValidationErrorCode[] ErrorCodes(MapSnapshotResult result) =>
        result.Errors.Select(e => e.Code).ToArray();

    [Fact]
    public void 正しいマップからスナップショットを作れる()
    {
        // A ─ B ─ T（受け渡し、充電ピンあり）
        //     │
        //     S（待機）
        var panels = new[] { Normal("A"), Normal("B"), Transfer("T", chargingPin: true), Standby("S") };
        var connections = new[]
        {
            Connect("A", "B", heading: 0),
            Connect("B", "T", heading: 0),
            Connect("B", "S", heading: 270),
        };

        var result = MapSnapshot.Create(panels, connections);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal(panels, result.Snapshot!.Panels);
        Assert.Equal(connections, result.Snapshot.Connections);
    }

    [Fact]
    public void パネルのIDは重複できない()
    {
        var result = MapSnapshot.Create([Normal("A"), Normal("A")], []);

        Assert.False(result.IsValid);
        Assert.Null(result.Snapshot);
        Assert.Equal([MapValidationErrorCode.DuplicatePanelId], ErrorCodes(result));
    }

    [Fact]
    public void つながりは存在するパネルどうしでなければならない()
    {
        var result = MapSnapshot.Create([Normal("A")], [Connect("A", "X")]);

        Assert.Equal([MapValidationErrorCode.UnknownPanel], ErrorCodes(result));
    }

    [Fact]
    public void パネルを自分自身につなげない()
    {
        var result = MapSnapshot.Create([Normal("A")], [Connect("A", "A")]);

        Assert.Equal([MapValidationErrorCode.SelfConnection], ErrorCodes(result));
    }

    [Theory]
    [InlineData("A", "B")]
    [InlineData("B", "A")]
    public void 同じパネルの組を二重につなげない(string from, string to)
    {
        var result = MapSnapshot.Create([Normal("A"), Normal("B")], [Connect("A", "B"), Connect(from, to, heading: 180)]);

        Assert.Equal([MapValidationErrorCode.DuplicateConnection], ErrorCodes(result));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-800)]
    public void 距離は正の値でなければならない(int distanceMm)
    {
        var result = MapSnapshot.Create([Normal("A"), Normal("B")], [Connect("A", "B", distanceMm: distanceMm)]);

        Assert.Equal([MapValidationErrorCode.NonPositiveDistance], ErrorCodes(result));
    }

    [Fact]
    public void 通常パネルには充電ピンを設置できない()
    {
        var panel = new Panel(new PanelId("A"), PanelType.Normal, HasChargingPin: true);

        var result = MapSnapshot.Create([panel], []);

        Assert.Equal([MapValidationErrorCode.ChargingPinOnNormalPanel], ErrorCodes(result));
    }

    [Fact]
    public void 受け渡しパネルと待機パネルには充電ピンを設置できる()
    {
        var result = MapSnapshot.Create(
            [Normal("A"), Transfer("T", chargingPin: true), Standby("S", chargingPin: true)],
            [Connect("A", "T", heading: 0), Connect("A", "S", heading: 90)]);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(PanelType.Transfer)]
    [InlineData(PanelType.Standby)]
    public void 受け渡しパネルと待機パネルは行き止まりでつながりは1つだけ(PanelType type)
    {
        var deadEnd = new Panel(new PanelId("D"), type, HasChargingPin: false);

        var result = MapSnapshot.Create(
            [Normal("A"), Normal("B"), deadEnd],
            [Connect("A", "D", heading: 0), Connect("D", "B", heading: 0)]);

        Assert.Equal([MapValidationErrorCode.DeadEndMustHaveSingleConnection], ErrorCodes(result));
    }

    [Theory]
    [InlineData(PanelType.Transfer)]
    [InlineData(PanelType.Standby)]
    public void 受け渡しパネルと待機パネルはどこかにつながっていなければならない(PanelType type)
    {
        var deadEnd = new Panel(new PanelId("D"), type, HasChargingPin: false);

        var result = MapSnapshot.Create([deadEnd], []);

        Assert.Equal([MapValidationErrorCode.DeadEndMustHaveSingleConnection], ErrorCodes(result));
    }

    [Fact]
    public void 受け渡しパネルと待機パネルの隣は通常パネルでなければならない()
    {
        var result = MapSnapshot.Create([Transfer("T"), Standby("S")], [Connect("T", "S")]);

        Assert.Equal(
            [MapValidationErrorCode.DeadEndMustConnectToNormalPanel, MapValidationErrorCode.DeadEndMustConnectToNormalPanel],
            ErrorCodes(result));
    }

    [Fact]
    public void 受け渡しパネルと待機パネルへのつながりは一方通行にできない()
    {
        // 前進で入り、後退で出るため、両方向に通れる必要がある
        var result = MapSnapshot.Create([Normal("A"), Transfer("T")], [Connect("A", "T", oneWay: true)]);

        // T から出られないので「行き来できない」の違反も出る
        Assert.Contains(MapValidationErrorCode.OneWayToDeadEnd, ErrorCodes(result));
    }

    [Fact]
    public void 違反はすべてまとめて返す()
    {
        var result = MapSnapshot.Create(
            [Normal("A"), Normal("A"), new Panel(new PanelId("B"), PanelType.Normal, HasChargingPin: true)],
            [Connect("A", "X")]);

        Assert.Equal(
            [
                MapValidationErrorCode.DuplicatePanelId,
                MapValidationErrorCode.ChargingPinOnNormalPanel,
                MapValidationErrorCode.UnknownPanel,
            ],
            ErrorCodes(result));
    }

    [Fact]
    public void 違反にはどのパネルかがわかるメッセージが付く()
    {
        var result = MapSnapshot.Create([Normal("A")], [Connect("A", "X")]);

        Assert.Contains("X", result.Errors.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void スナップショットは作ったあとに元のリストを変えても影響を受けない()
    {
        var panels = new List<Panel> { Normal("A") };

        var snapshot = MapSnapshot.Create(panels, []).Snapshot!;
        panels.Add(Normal("B"));

        Assert.Single(snapshot.Panels);
    }
}
