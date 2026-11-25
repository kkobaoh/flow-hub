using FlowHub.Dispatching.Domain;
using FlowHub.Map;
using FlowHub.Traffic;

namespace FlowHub.Dispatching.Tests;

public class AgvChargingTests
{
    private static readonly ChargingPolicy Policy = new(GoChargeBelowPercent: 20, ResumeAtPercent: 80);
    private static readonly PanelId Port = new("CP");

    private static readonly AgvSituation IdleElsewhere = new(OnChargingPinPanel: false, HasActiveTransport: false);
    private static readonly AgvSituation BusyElsewhere = new(OnChargingPinPanel: false, HasActiveTransport: true);
    private static readonly AgvSituation OnPin = new(OnChargingPinPanel: true, HasActiveTransport: false);

    private static AgvCharging NewCharging() => new(new AgvId("AGV-1"), Policy);

    [Fact]
    public void 充電の値を変えたら次の充電残量の知らせから新しい値で判断する()
    {
        var charging = NewCharging();
        Assert.Equal(ChargingDecision.None, charging.OnBatteryChanged(50, IdleElsewhere));

        charging.ChangePolicy(new ChargingPolicy(GoChargeBelowPercent: 60, ResumeAtPercent: 90));

        Assert.Equal(ChargingDecision.FindPort, charging.OnBatteryChanged(50, IdleElsewhere));
    }

    [Fact]
    public void はじめは通常で割り当てに使える()
    {
        var charging = NewCharging();

        Assert.Equal(ChargingState.Normal, charging.State);
        Assert.False(charging.RecoveringCharge);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(50)]
    public void 充電に向かう値を下回らなければ何もしない(int battery)
    {
        var charging = NewCharging();

        var decision = charging.OnBatteryChanged(battery, IdleElsewhere);

        Assert.Equal(ChargingDecision.None, decision);
        Assert.Equal(ChargingState.Normal, charging.State);
    }

    [Fact]
    public void 待機中に下回ったら充電ポートを探す()
    {
        var charging = NewCharging();

        var decision = charging.OnBatteryChanged(19, IdleElsewhere);

        Assert.Equal(ChargingDecision.FindPort, decision);
        Assert.True(charging.RecoveringCharge);
    }

    [Fact]
    public void 搬送の途中で下回ったら搬送を終えてから充電ポートを探す()
    {
        var charging = NewCharging();

        var decision = charging.OnBatteryChanged(19, BusyElsewhere);

        Assert.Equal(ChargingDecision.None, decision);
        Assert.Equal(ChargingState.PendingAfterTransport, charging.State);
        Assert.True(charging.RecoveringCharge); // ほかの搬送はもう割り当てない
        Assert.Equal(ChargingDecision.FindPort, charging.OnTransportFinished(IdleElsewhere));
    }

    [Fact]
    public void 搬送の途中なら充電ピンのあるパネルにいても搬送を終えてから充電する()
    {
        var charging = NewCharging();

        var decision = charging.OnBatteryChanged(19, new AgvSituation(OnChargingPinPanel: true, HasActiveTransport: true));

        Assert.Equal(ChargingDecision.None, decision);
        Assert.Equal(ChargingState.PendingAfterTransport, charging.State);
    }

    [Fact]
    public void 搬送を終えたパネルに充電ピンがあればその場で充電する()
    {
        var charging = NewCharging();
        charging.OnBatteryChanged(19, BusyElsewhere);

        var decision = charging.OnTransportFinished(OnPin);

        Assert.Equal(ChargingDecision.ChargeInPlace, decision);
        Assert.Equal(ChargingState.Charging, charging.State);
    }

    [Fact]
    public void 充電ピンのあるパネルで下回ったらその場で充電する()
    {
        var charging = NewCharging();

        var decision = charging.OnBatteryChanged(19, OnPin);

        Assert.Equal(ChargingDecision.ChargeInPlace, decision);
        Assert.Equal(ChargingState.Charging, charging.State);
    }

    [Fact]
    public void 充電ポートへ向かい着いたら充電する()
    {
        var charging = NewCharging();
        charging.OnBatteryChanged(19, IdleElsewhere);

        charging.GoTo(Port);
        Assert.Equal(ChargingState.GoingToPort, charging.State);

        var decision = charging.OnArrived(Port);
        Assert.Equal(ChargingDecision.None, decision);
        Assert.Equal(ChargingState.Charging, charging.State);
    }

    [Fact]
    public void 割り当てに戻す値まで回復したら通常に戻る()
    {
        var charging = NewCharging();
        charging.OnBatteryChanged(19, OnPin);

        Assert.Equal(ChargingDecision.None, charging.OnBatteryChanged(79, OnPin));
        var decision = charging.OnBatteryChanged(80, OnPin);

        Assert.Equal(ChargingDecision.Recovered, decision);
        Assert.Equal(ChargingState.Normal, charging.State);
        Assert.False(charging.RecoveringCharge);
    }

    [Fact]
    public void 向かう途中で回復しても向かうのはやめず着いてから判断する()
    {
        var charging = NewCharging();
        charging.OnBatteryChanged(19, IdleElsewhere);
        charging.GoTo(Port);

        Assert.Equal(ChargingDecision.None, charging.OnBatteryChanged(85, IdleElsewhere));
        Assert.Equal(ChargingState.GoingToPort, charging.State);

        var decision = charging.OnArrived(Port);
        Assert.Equal(ChargingDecision.Recovered, decision);
        Assert.Equal(ChargingState.Normal, charging.State);
    }

    [Fact]
    public void 向かっている充電ポートでないパネルへの到着では何もしない()
    {
        var charging = NewCharging();
        charging.OnBatteryChanged(19, IdleElsewhere);
        charging.GoTo(Port);

        var decision = charging.OnArrived(new PanelId("X"));

        Assert.Equal(ChargingDecision.None, decision);
        Assert.Equal(ChargingState.GoingToPort, charging.State);
    }

    [Fact]
    public void 充電ポートへ向かう途中で異常になったら充電ポートを探すに戻る()
    {
        var charging = NewCharging();
        charging.OnBatteryChanged(19, IdleElsewhere);
        charging.GoTo(Port);

        charging.OnFaulted();

        Assert.Equal(ChargingState.SeekingPort, charging.State);
        Assert.Null(charging.Port);
    }

    [Fact]
    public void 充電ポートへ向かっていないときに異常になっても充電の状態は変えない()
    {
        var charging = NewCharging();
        charging.OnBatteryChanged(19, OnPin);

        charging.OnFaulted();

        Assert.Equal(ChargingState.Charging, charging.State);
    }

    [Fact]
    public void 行ける充電ポートがなければ待つ()
    {
        var charging = NewCharging();
        charging.OnBatteryChanged(19, IdleElsewhere);

        charging.WaitForPort();

        Assert.Equal(ChargingState.WaitingForPort, charging.State);
        Assert.True(charging.RecoveringCharge);
    }

    [Fact]
    public void 待っている間に充電ポートが空いたら向かう()
    {
        var charging = NewCharging();
        charging.OnBatteryChanged(19, IdleElsewhere);
        charging.WaitForPort();

        charging.GoTo(Port);

        Assert.Equal(ChargingState.GoingToPort, charging.State);
    }

    [Fact]
    public void 充電ポートを探していないときは向かわせられない()
    {
        var charging = NewCharging();

        Assert.Throws<InvalidOperationException>(() => charging.GoTo(Port));
        Assert.Throws<InvalidOperationException>(() => charging.WaitForPort());
    }

    [Fact]
    public void 充電を待っていないときに搬送を終えても何もしない()
    {
        var charging = NewCharging();

        Assert.Equal(ChargingDecision.None, charging.OnTransportFinished(IdleElsewhere));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void 充電残量は0から100の間でなければならない(int battery)
    {
        var charging = NewCharging();

        Assert.Throws<ArgumentOutOfRangeException>(() => charging.OnBatteryChanged(battery, IdleElsewhere));
    }

    [Fact]
    public void 充電残量の最新の値を覚える()
    {
        var charging = NewCharging();

        charging.OnBatteryChanged(55, IdleElsewhere);

        Assert.Equal(55, charging.BatteryPercent);
    }
}
