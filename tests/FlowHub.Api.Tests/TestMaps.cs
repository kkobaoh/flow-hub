using FlowHub.Map;

namespace FlowHub.Api.Tests;

/// <summary>テストで使う、描く情報つきのマップ。</summary>
internal static class TestMaps
{
    /// <summary>A（通常パネル、列 0）→（東へ 1000mm）T（受け渡しパネル、列 1）。T の下に外形がある。</summary>
    public static MapSnapshot TwoPanels() =>
        MapSnapshot.Create(
            [new Panel(new PanelId("A"), PanelType.Normal, false), new Panel(new PanelId("T"), PanelType.Transfer, false)],
            [new Connection(new PanelId("A"), new PanelId("T"), 1000, new Heading(90), OneWay: false)],
            drawing: new MapDrawing(1000, new Dictionary<PanelId, PanelPlacement>
            {
                [new PanelId("A")] = new(new GridPosition(0, 0), CrossOffset.None, null),
                [new PanelId("T")] = new(new GridPosition(1, 0), CrossOffset.None, new Outline(1500, -800, 2000, 1600)),
            })).Snapshot!;
}
