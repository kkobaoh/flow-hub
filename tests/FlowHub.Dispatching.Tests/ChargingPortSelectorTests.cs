using FlowHub.Dispatching.Domain;
using FlowHub.Map;

namespace FlowHub.Dispatching.Tests;

public class ChargingPortSelectorTests
{
    private static readonly PanelId Near = new("P-near");
    private static readonly PanelId Middle = new("P-middle");
    private static readonly PanelId Far = new("P-far");

    private static Func<PanelId, TimeSpan?> Costs(params (PanelId Port, double Seconds)[] costs)
    {
        var map = costs.ToDictionary(c => c.Port, c => TimeSpan.FromSeconds(c.Seconds));
        return port => map.TryGetValue(port, out var cost) ? cost : null;
    }

    [Fact]
    public void 一番近い充電ポートを選ぶ()
    {
        var chosen = ChargingPortSelector.Choose(
            [Far, Near, Middle],
            isTakenByOther: _ => false,
            Costs((Near, 10), (Middle, 20), (Far, 30)));

        Assert.Equal(Near, chosen);
    }

    [Fact]
    public void 一番近い充電ポートをほかのAGVが使っていたら次に近い空いている充電ポートを選ぶ()
    {
        var chosen = ChargingPortSelector.Choose(
            [Near, Middle, Far],
            isTakenByOther: port => port == Near,
            Costs((Near, 10), (Middle, 20), (Far, 30)));

        Assert.Equal(Middle, chosen);
    }

    [Fact]
    public void 経路が見つからない充電ポートは選ばない()
    {
        var chosen = ChargingPortSelector.Choose(
            [Near, Middle],
            isTakenByOther: _ => false,
            Costs((Middle, 20)));

        Assert.Equal(Middle, chosen);
    }

    [Fact]
    public void 行ける充電ポートがなければ選ばない()
    {
        var chosen = ChargingPortSelector.Choose(
            [Near],
            isTakenByOther: _ => true,
            Costs((Near, 10)));

        Assert.Null(chosen);
    }
}
