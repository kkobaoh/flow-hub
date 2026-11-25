namespace FlowHub.Map.Tests;

/// <summary>
/// 下書きを、公開するときと同じに検証するテスト（下書き → スナップショット）。
/// </summary>
public class MapDraftValidationTests
{
    private static DraftPanel Normal(string id, int column) => new(id, "Normal", false, null, column, 0, 0, 0, null);

    /// <summary>P1 ─ P2 ─ D1（受け渡しパネル、外形あり）。</summary>
    private static MapDraft Valid() => new(
        1000,
        [Normal("P1", 0), Normal("P2", 1), new DraftPanel("D1", "Transfer", true, null, 2, 0, 0, 0, new DraftOutline(1500, -800, 2000, 1600))],
        [new DraftConnection("P1", "P2", 1000, 90, false), new DraftConnection("P2", "D1", 1000, 90, false)]);

    [Fact]
    public void 問題のない下書きはスナップショットになり中身はそのまま()
    {
        var result = Valid().ToSnapshot(MapValidationOptions.Default);

        Assert.Empty(result.Errors);
        var snapshot = Assert.IsType<MapSnapshot>(result.Snapshot);
        Assert.Equal(["P1", "P2", "D1"], snapshot.Panels.Select(p => p.Id.Value));
        Assert.True(Valid().SameAs(MapDraft.FromSnapshot(snapshot)));
    }

    [Fact]
    public void 公開するときと同じ検証の違反をすべて返す()
    {
        var draft = Valid() with { Connections = [new DraftConnection("P1", "P2", 1000, 90, false), new DraftConnection("P2", "NOWHERE", 1000, 90, false)] };

        var result = draft.ToSnapshot(MapValidationOptions.Default);

        Assert.Null(result.Snapshot);
        Assert.Contains(result.Errors, e => e.Code == MapValidationErrorCode.UnknownPanel);
    }

    [Fact]
    public void 同じIDのパネルは重複として返す()
    {
        var draft = Valid() with { Panels = [.. Valid().Panels, Normal("P1", 5)] };

        var result = draft.ToSnapshot(MapValidationOptions.Default);

        Assert.Contains(result.Errors, e => e.Code == MapValidationErrorCode.DuplicatePanelId);
    }

    [Fact]
    public void 値の型にできない値は正しくない値として返し理由がわかる()
    {
        var draft = new MapDraft(
            0,
            [new DraftPanel(" ", "Wall", false, 400, 0, 0, 0, 0, new DraftOutline(0, 0, 0, 10))],
            [new DraftConnection("P1", "P2", 1000, -90, false)]);

        var result = draft.ToSnapshot(MapValidationOptions.Default);

        Assert.Null(result.Snapshot);
        Assert.All(result.Errors, e => Assert.Equal(MapValidationErrorCode.InvalidValue, e.Code));
        Assert.Equal(
            [
                "パネルの一辺の長さは、1 以上にしてください（0）。",
                "1 枚目のパネルの ID がありません。",
                "パネル「 」の種類がわかりません（Wall）。",
                "パネル「 」のキャリアを下ろす角度は、0 以上 360 未満にしてください（400）。",
                "パネル「 」の外形の幅と奥行きは、1 以上にしてください。",
                "P1 から P2 へのつながりの向きは、0 以上 360 未満にしてください（-90）。",
            ],
            result.Errors.Select(e => e.Message));
    }
}
