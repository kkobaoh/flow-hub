using FlowHub.Dispatching.Domain;
using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic;

namespace FlowHub.Dispatching.Tests;

public class AgvAssignerTests
{
    private static readonly CarrierTypeId CarrierX = new("X");
    private static readonly CarrierTypeId CarrierY = new("Y");

    private static readonly ChargingPolicy Policy = new(GoChargeBelowPercent: 20, ResumeAtPercent: 80);

    private static readonly AssignmentRequest Request = new(new PanelId("T1"), CarrierX);

    private static AgvCandidate Agv(
        string id,
        bool isIdle = true,
        int battery = 100,
        bool recoveringCharge = false,
        CarrierTypeId? carrier = null,
        long cumulativeDistanceMm = 0) =>
        new(new AgvId(id), new Pose("A", 0), isIdle, battery, recoveringCharge, carrier ?? CarrierX, cumulativeDistanceMm);

    /// <summary>AGV の ID ごとの移動コスト（秒）。載っていない AGV は経路なし。</summary>
    private static Func<AgvCandidate, TimeSpan?> Costs(params (string Id, double Seconds)[] costs)
    {
        var map = costs.ToDictionary(c => c.Id, c => TimeSpan.FromSeconds(c.Seconds));
        return candidate => map.TryGetValue(candidate.Id.Value, out var cost) ? cost : null;
    }

    private static AgvId? Choose(IEnumerable<AgvCandidate> candidates, Func<AgvCandidate, TimeSpan?> costOf) =>
        AgvAssigner.Choose(Request, candidates, Policy, costOf);

    [Fact]
    public void 搬送元に一番近いAGVを選ぶ()
    {
        var chosen = Choose([Agv("AGV-1"), Agv("AGV-2"), Agv("AGV-3")], Costs(("AGV-1", 30), ("AGV-2", 10), ("AGV-3", 20)));

        Assert.Equal(new AgvId("AGV-2"), chosen);
    }

    [Fact]
    public void 近さが同じなら累計の走行距離が短いAGVを選ぶ()
    {
        var chosen = Choose(
            [Agv("AGV-1", cumulativeDistanceMm: 5000), Agv("AGV-2", cumulativeDistanceMm: 1000)],
            Costs(("AGV-1", 10), ("AGV-2", 10)));

        Assert.Equal(new AgvId("AGV-2"), chosen);
    }

    [Fact]
    public void 近さも累計の走行距離も同じならAGVのIDの順で選ぶ()
    {
        var chosen = Choose([Agv("AGV-2"), Agv("AGV-1")], Costs(("AGV-1", 10), ("AGV-2", 10)));

        Assert.Equal(new AgvId("AGV-1"), chosen);
    }

    [Fact]
    public void 待機中でないAGVには割り当てない()
    {
        var chosen = Choose([Agv("AGV-1", isIdle: false), Agv("AGV-2")], Costs(("AGV-1", 10), ("AGV-2", 30)));

        Assert.Equal(new AgvId("AGV-2"), chosen);
    }

    [Fact]
    public void キャリアの種類が合わないAGVには割り当てない()
    {
        var chosen = Choose([Agv("AGV-1", carrier: CarrierY), Agv("AGV-2")], Costs(("AGV-1", 10), ("AGV-2", 30)));

        Assert.Equal(new AgvId("AGV-2"), chosen);
    }

    [Fact]
    public void 搬送元までの経路が見つからないAGVには割り当てない()
    {
        var chosen = Choose([Agv("AGV-1"), Agv("AGV-2")], Costs(("AGV-2", 30)));

        Assert.Equal(new AgvId("AGV-2"), chosen);
    }

    [Theory]
    [InlineData(19, false)]
    [InlineData(20, true)]
    [InlineData(21, true)]
    public void 充電残量が充電に向かう値を下回っていたら割り当てない(int battery, bool assignable)
    {
        var chosen = Choose([Agv("AGV-1", battery: battery)], Costs(("AGV-1", 10)));

        Assert.Equal(assignable, chosen is not null);
    }

    [Theory]
    [InlineData(79, false)]
    [InlineData(80, true)]
    public void 充電に向かったAGVは割り当てに戻す値まで回復するまで割り当てない(int battery, bool assignable)
    {
        var chosen = Choose([Agv("AGV-1", battery: battery, recoveringCharge: true)], Costs(("AGV-1", 10)));

        Assert.Equal(assignable, chosen is not null);
    }

    [Fact]
    public void 割り当てられるAGVがいなければ選ばない()
    {
        var chosen = Choose([Agv("AGV-1", isIdle: false)], Costs(("AGV-1", 10)));

        Assert.Null(chosen);
    }

    [Fact]
    public void 割り当てられないAGVの移動コストは求めない()
    {
        // 移動コストの計算は経路探索なので重い。割り当てられない AGV では呼ばない
        var asked = new List<string>();

        Choose([Agv("AGV-1", isIdle: false), Agv("AGV-2")], candidate =>
        {
            asked.Add(candidate.Id.Value);
            return TimeSpan.FromSeconds(10);
        });

        Assert.Equal(["AGV-2"], asked);
    }
}
