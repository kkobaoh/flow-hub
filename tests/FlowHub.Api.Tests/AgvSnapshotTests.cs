using FlowHub.Api.Composition.Screens;
using FlowHub.Consumables;
using FlowHub.Dispatching;
using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic;

namespace FlowHub.Api.Tests;

/// <summary>
/// 画面へ送る AGV の様子の組み立てと比べ方のテスト。
/// </summary>
public class AgvSnapshotTests
{
    private static readonly AgvId Agv1 = new("AGV-1");

    private static AgvView View() =>
        new(Agv1, new Pose("P3", 90), AgvState.Moving, CarrierRaised: true, CumulativeDistanceMm: 0)
        {
            ReservedPanels = [new PanelId("P3"), new PanelId("P4")],
            RoutePanels = [new PanelId("P3"), new PanelId("P4"), new PanelId("D1")],
        };

    [Fact]
    public void 予約しているパネルと経路と割り当て済みの搬送を入れる()
    {
        var assignment = new AgvAssignment(
            Agv1,
            new TransportId(12),
            new SourceTransferPoint(new PanelId("ST"), "station:ST-1"),
            new PanelId("D1"),
            "device:DEV-1",
            AssignedTransportStage.MovingToDestination,
            Paused: true);

        var snapshot = AgvSnapshot.From(View(), assignment, TransferEndpointNames.DisplayName);

        Assert.Equal(["P3", "P4"], snapshot.ReservedPanels);
        Assert.Equal(["P3", "P4", "D1"], snapshot.Route);
        Assert.Equal(new AgvTransportSnapshot(12, "ST-1", "DEV-1", "movingToDestination", Paused: true), snapshot.Transport);
    }

    [Fact]
    public void ものを積んだまま止まっていたAGVが搬送元ならAGVの名前を搬送元にする()
    {
        var assignment = new AgvAssignment(Agv1, new TransportId(13), new SourceAgv(Agv1), new PanelId("D1"), "device:DEV-1", AssignedTransportStage.Interrupted, Paused: false);

        var snapshot = AgvSnapshot.From(View(), assignment, TransferEndpointNames.DisplayName);

        Assert.Equal(new AgvTransportSnapshot(13, "AGV-1", "DEV-1", "interrupted", Paused: false), snapshot.Transport);
    }

    [Fact]
    public void 割り当て済みの搬送がなければ搬送はない()
    {
        Assert.Null(AgvSnapshot.From(View(), assignment: null, TransferEndpointNames.DisplayName).Transport);
    }

    [Fact]
    public void 並びの中身が同じなら同じ様子とみなす()
    {
        var a = AgvSnapshot.From(View(), assignment: null, TransferEndpointNames.DisplayName);
        var b = AgvSnapshot.From(View(), assignment: null, TransferEndpointNames.DisplayName);
        var moved = AgvSnapshot.From(View() with { RoutePanels = [new PanelId("P4"), new PanelId("D1")] }, assignment: null, TransferEndpointNames.DisplayName);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, moved);
    }
}
