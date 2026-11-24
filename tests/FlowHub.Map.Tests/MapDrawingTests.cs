namespace FlowHub.Map.Tests;

/// <summary>
/// 画面に描くための情報（パネルの位置、十字のずれ、受け渡しパネルの下にあるものの外形）と、その検証のテスト。
/// </summary>
public class MapDrawingTests
{
    private const int Size = 1000;

    private static Panel Normal(string id) => new(new PanelId(id), PanelType.Normal, HasChargingPin: false);

    private static Panel Transfer(string id) => new(new PanelId(id), PanelType.Transfer, HasChargingPin: false);

    private static Connection Connect(string from, string to, int distanceMm = 1000, int heading = 90) =>
        new(new PanelId(from), new PanelId(to), distanceMm, new Heading(heading), OneWay: false);

    private static KeyValuePair<PanelId, PanelPlacement> At(string id, int column, int row, int eastMm = 0, int southMm = 0, Outline? outline = null) =>
        new(new PanelId(id), new PanelPlacement(new GridPosition(column, row), new CrossOffset(eastMm, southMm), outline));

    private static MapDrawing Drawing(params KeyValuePair<PanelId, PanelPlacement>[] placements) => new(Size, new Dictionary<PanelId, PanelPlacement>(placements));

    private static MapValidationErrorCode[] Codes(MapSnapshotResult result) => result.Errors.Select(e => e.Code).ToArray();

    [Fact]
    public void 描く情報を持ったスナップショットを作れる()
    {
        var outline = new Outline(500, 1200, 2000, 1500);
        var result = MapSnapshot.Create(
            [Normal("A"), Transfer("T")],
            [Connect("A", "T", heading: 180)],
            drawing: Drawing(At("A", 0, 0), At("T", 0, 1, outline: outline)));

        Assert.True(result.IsValid, string.Join(" / ", result.Errors.Select(e => e.Message)));
        var drawing = result.Snapshot!.Drawing!;
        Assert.Equal(Size, drawing.PanelSizeMm);
        Assert.Equal(new GridPosition(0, 1), drawing.Placements[new PanelId("T")].Position);
        Assert.Equal(outline, drawing.Placements[new PanelId("T")].Outline);
    }

    [Fact]
    public void 描く情報がなくてもスナップショットは作れる()
    {
        var result = MapSnapshot.Create([Normal("A"), Normal("B")], [Connect("A", "B", distanceMm: 100)]);

        Assert.True(result.IsValid);
        Assert.Null(result.Snapshot!.Drawing);
    }

    [Fact]
    public void 位置のないパネルとマップにないパネルの位置はエラー()
    {
        var result = MapSnapshot.Create(
            [Normal("A"), Normal("B")],
            [Connect("A", "B")],
            drawing: Drawing(At("A", 0, 0), At("Z", 5, 5)));

        Assert.Contains(MapValidationErrorCode.MissingPlacement, Codes(result));
        Assert.Contains(MapValidationErrorCode.PlacementForUnknownPanel, Codes(result));
    }

    [Fact]
    public void 同じ格子に2枚のパネルはエラー()
    {
        var result = MapSnapshot.Create(
            [Normal("A"), Normal("B")],
            [Connect("A", "B")],
            drawing: Drawing(At("A", 0, 0), At("B", 0, 0)));

        Assert.Equal([MapValidationErrorCode.DuplicateGridPosition], Codes(result));
    }

    [Fact]
    public void 外形は受け渡しパネルと特殊消耗品受け渡しパネルにだけ置ける()
    {
        var result = MapSnapshot.Create(
            [Normal("A"), Normal("B")],
            [Connect("A", "B")],
            drawing: Drawing(At("A", 0, 0, outline: new Outline(0, 0, 100, 100)), At("B", 1, 0)));

        Assert.Equal([MapValidationErrorCode.OutlineOnNonTransferPanel], Codes(result));
    }

    [Fact]
    public void 上下左右に隣り合う交点の間が最小の距離より短ければエラーつながっていない組も位置から確かめる()
    {
        // A と B はつながっていないが、上下に隣り合っている。B の十字が北へ 400mm ずれているので、交点の間は 600mm
        var result = MapSnapshot.Create(
            [Normal("A"), Normal("B"), Normal("C")],
            [Connect("A", "C"), Connect("B", "C", heading: 0)],
            drawing: Drawing(At("A", 0, 0), At("B", 0, 1, southMm: -400), At("C", 1, 0, southMm: 0)));

        var error = Assert.Single(result.Errors, e => e.Code == MapValidationErrorCode.CrossDistanceTooShort);
        Assert.Contains("A", error.Message, StringComparison.Ordinal);
        Assert.Contains("B", error.Message, StringComparison.Ordinal);
        Assert.Contains("600", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void つながっている組はつながりの距離も最小の距離以上でなければエラー()
    {
        var result = MapSnapshot.Create(
            [Normal("A"), Normal("B")],
            [Connect("A", "B", distanceMm: 640)],
            drawing: Drawing(At("A", 0, 0), At("B", 1, 0)));

        var error = Assert.Single(result.Errors);
        Assert.Equal(MapValidationErrorCode.ConnectionDistanceTooShort, error.Code);
        Assert.Contains("640", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 最小の距離はちょうどならよくオプションで変えられる()
    {
        // 十字が東へ 350mm ずれて、交点の間がちょうど 650mm
        var exactly = MapSnapshot.Create(
            [Normal("A"), Normal("B")],
            [Connect("A", "B", distanceMm: 650)],
            drawing: Drawing(At("A", 0, 0, eastMm: 350), At("B", 1, 0)));
        Assert.True(exactly.IsValid, string.Join(" / ", exactly.Errors.Select(e => e.Message)));

        var stricter = MapSnapshot.Create(
            [Normal("A"), Normal("B")],
            [Connect("A", "B", distanceMm: 650)],
            new MapValidationOptions { MinimumCrossDistanceMm = 700 },
            Drawing(At("A", 0, 0, eastMm: 350), At("B", 1, 0)));
        Assert.Contains(MapValidationErrorCode.CrossDistanceTooShort, Codes(stricter));
        Assert.Contains(MapValidationErrorCode.ConnectionDistanceTooShort, Codes(stricter));
    }

    [Fact]
    public void 値の型は作るときに確かめる()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Outline(0, 0, 0, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Outline(0, 0, 100, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapDrawing(0, new Dictionary<PanelId, PanelPlacement>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapValidationOptions { MinimumCrossDistanceMm = 0 });
    }
}
