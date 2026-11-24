using FlowHub.Consumables.Domain;

namespace FlowHub.Consumables.Tests;

public class LocationsTests
{
    [Fact]
    public void スロットはストッカーの番号の順に同じストッカーの中ではスロットの番号の順に比べる()
    {
        var slots = new[] { new SlotRef(2, 1), new SlotRef(1, 2), new SlotRef(1, 1) };

        Assert.Equal([new SlotRef(1, 1), new SlotRef(1, 2), new SlotRef(2, 1)], slots.Order());
        Assert.True(new SlotRef(1, 2) < new SlotRef(2, 1));
        Assert.True(new SlotRef(2, 1) > new SlotRef(1, 2));
        Assert.True(new SlotRef(1, 1) <= new SlotRef(1, 1));
        Assert.True(new SlotRef(1, 2) >= new SlotRef(1, 1));
    }

    [Fact]
    public void 装置のすべてのスロットを番号の順に並べる()
    {
        var layout = new DeviceLayout(new DeviceId("D1"), new Map.PanelId("D"), SlotsPerStocker: [2, 1]);

        Assert.Equal([new SlotRef(1, 1), new SlotRef(1, 2), new SlotRef(2, 1)], layout.AllSlots);
    }

    [Fact]
    public void 受け渡しの相手の参照を読み取る()
    {
        Assert.Equal((new DeviceId("D1"), (StationId?)null), Endpoints.Parse(Endpoints.Device(new DeviceId("D1"))));
        Assert.Equal(((DeviceId?)null, new StationId("S1")), Endpoints.Parse(Endpoints.Station(new StationId("S1"))));
        Assert.Throws<ArgumentException>(() => Endpoints.Parse("unknown:X"));
    }
}
