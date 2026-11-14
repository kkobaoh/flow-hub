using FlowHub.Map;
using FlowHub.Traffic.Domain;

namespace FlowHub.Traffic.Tests;

public class PanelReservationsTests
{
    private static readonly AgvId Agv1 = new("AGV-1");
    private static readonly AgvId Agv2 = new("AGV-2");

    private static PanelId[] Panels(params string[] ids) => ids.Select(id => new PanelId(id)).ToArray();

    [Fact]
    public void 空いているパネルはまとめて予約できる()
    {
        var reservations = new PanelReservations();

        var result = reservations.TryReserve(Agv1, Panels("A", "B", "C"));

        Assert.True(result.Succeeded);
        Assert.Equal(Agv1, reservations.OwnerOf(new PanelId("B")));
    }

    [Fact]
    public void 一部でもほかのAGVが予約していたら何も予約しない()
    {
        var reservations = new PanelReservations();
        reservations.TryReserve(Agv2, Panels("C"));

        var result = reservations.TryReserve(Agv1, Panels("A", "B", "C"));

        Assert.False(result.Succeeded);
        Assert.Equal(Panels("C"), result.Conflicts);
        Assert.Null(reservations.OwnerOf(new PanelId("A")));
        Assert.Null(reservations.OwnerOf(new PanelId("B")));
    }

    [Fact]
    public void 予約できなかったときはぶつかったパネルと相手がわかる()
    {
        var reservations = new PanelReservations();
        reservations.TryReserve(Agv2, Panels("B", "C"));

        var result = reservations.TryReserve(Agv1, Panels("A", "B", "C"));

        Assert.Equal(Panels("B", "C"), result.Conflicts);
        Assert.Equal([Agv2], result.BlockingAgvs);
    }

    [Fact]
    public void 自分がすでに予約しているパネルは重ねて予約できる()
    {
        var reservations = new PanelReservations();
        reservations.TryReserve(Agv1, Panels("A"));

        var result = reservations.TryReserve(Agv1, Panels("A", "B"));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void 解放したパネルはほかのAGVが予約できる()
    {
        var reservations = new PanelReservations();
        reservations.TryReserve(Agv1, Panels("A", "B"));

        reservations.Release(Agv1, new PanelId("A"));

        Assert.Null(reservations.OwnerOf(new PanelId("A")));
        Assert.True(reservations.TryReserve(Agv2, Panels("A")).Succeeded);
    }

    [Fact]
    public void ほかのAGVの予約は解放できない()
    {
        var reservations = new PanelReservations();
        reservations.TryReserve(Agv2, Panels("A"));

        Assert.Throws<InvalidOperationException>(() => reservations.Release(Agv1, new PanelId("A")));
        Assert.Equal(Agv2, reservations.OwnerOf(new PanelId("A")));
    }

    [Fact]
    public void 予約していないパネルの解放は何もしない()
    {
        var reservations = new PanelReservations();

        reservations.Release(Agv1, new PanelId("A"));

        Assert.Null(reservations.OwnerOf(new PanelId("A")));
    }

    [Fact]
    public void AGVの予約をすべて解放できる()
    {
        var reservations = new PanelReservations();
        reservations.TryReserve(Agv1, Panels("A", "B"));
        reservations.TryReserve(Agv2, Panels("C"));

        reservations.ReleaseAll(Agv1);

        Assert.Empty(reservations.ReservedBy(Agv1));
        Assert.Equal(Panels("C"), reservations.ReservedBy(Agv2));
    }

    [Fact]
    public void AGVが予約しているパネルを一覧できる()
    {
        var reservations = new PanelReservations();
        reservations.TryReserve(Agv1, Panels("A", "B"));

        Assert.Equal(Panels("A", "B"), reservations.ReservedBy(Agv1).OrderBy(p => p.Value, StringComparer.Ordinal).ToArray());
    }
}
