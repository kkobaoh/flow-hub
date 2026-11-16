using FlowHub.Dispatching.Domain;

namespace FlowHub.Dispatching.Tests;

public class ChargingPolicyTests
{
    [Theory]
    [InlineData(-1, 80)]
    [InlineData(101, 80)]
    [InlineData(20, -1)]
    [InlineData(20, 101)]
    public void 値は0から100の間でなければならない(int goChargeBelow, int resumeAt)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChargingPolicy(goChargeBelow, resumeAt));
    }

    [Fact]
    public void 割り当てに戻す値は充電に向かう値より小さくできない()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChargingPolicy(GoChargeBelowPercent: 80, ResumeAtPercent: 20));
    }

    [Fact]
    public void 割り当てに戻す値と充電に向かう値は同じでもよい()
    {
        var policy = new ChargingPolicy(GoChargeBelowPercent: 50, ResumeAtPercent: 50);

        Assert.Equal(50, policy.ResumeAtPercent);
    }

    [Fact]
    public void キャリアの種類のIDは空にできない()
    {
        Assert.Equal(new CarrierTypeId("X"), new CarrierTypeId("X"));
        Assert.Equal("X", new CarrierTypeId("X").ToString());
        Assert.Throws<ArgumentException>(() => new CarrierTypeId(" "));
    }
}
