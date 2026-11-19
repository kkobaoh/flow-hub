using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic.Domain;

namespace FlowHub.Traffic.Tests;

public class AgvCarrierTests
{
    private static readonly AgvId Agv1 = new("AGV-1");
    private const long NoneReceived = -1;

    private static readonly Panel Transfer = new(new PanelId("T"), PanelType.Transfer, HasChargingPin: false);
    private static readonly Panel Normal = new(new PanelId("A"), PanelType.Normal, HasChargingPin: false);

    /// <summary>
    /// 受け渡しパネル T で待機中の AGV（位置リセットの番号は 1）。
    /// </summary>
    private static AgvTravel IdleAtTransfer()
    {
        var travel = new AgvTravel(Agv1, new PanelReservations());
        travel.ResetPosition(new Pose("T", 0));
        travel.OnCommandRequest(NoneReceived);
        travel.OnPositionResetCompleted(1);
        return travel;
    }

    /// <summary>キャリアを下げ終えた AGV（下げるコマンドの番号は 2）。</summary>
    private static AgvTravel LoweredAtTransfer()
    {
        var travel = IdleAtTransfer();
        travel.LowerCarrier(Transfer, heightMm: 120);
        travel.OnCommandRequest(1);
        travel.OnCarrierLowered(2);
        return travel;
    }

    // ---- 下げる ----

    [Fact]
    public void 受け渡しパネルで待機中ならキャリアを下げるコマンドを高さを入れて用意する()
    {
        var travel = IdleAtTransfer();

        travel.LowerCarrier(Transfer, heightMm: 120);

        Assert.Equal(CarrierState.Lowering, travel.Carrier);
        Assert.Equal([new LowerCarrierCommand(2, 120)], travel.OnCommandRequest(1));
    }

    [Fact]
    public void 高さが設定されていなければ高さを入れずに渡してAGVに測定させる()
    {
        var travel = IdleAtTransfer();

        travel.LowerCarrier(Transfer, heightMm: null);

        Assert.Equal([new LowerCarrierCommand(2, null)], travel.OnCommandRequest(1));
    }

    [Fact]
    public void 受け渡しパネルでなければキャリアを下げられない()
    {
        var travel = new AgvTravel(Agv1, new PanelReservations());
        travel.ResetPosition(new Pose("A", 0));
        travel.OnCommandRequest(NoneReceived);
        travel.OnPositionResetCompleted(1);

        Assert.Throws<InvalidOperationException>(() => travel.LowerCarrier(Normal, heightMm: 120));
    }

    [Fact]
    public void 今いるパネルでなければキャリアを下げられない()
    {
        var travel = IdleAtTransfer();
        var other = new Panel(new PanelId("T2"), PanelType.Transfer, HasChargingPin: false);

        Assert.Throws<ArgumentException>(() => travel.LowerCarrier(other, heightMm: 120));
    }

    [Fact]
    public void 待機中でなければキャリアを下げられない()
    {
        var travel = IdleAtTransfer();
        travel.EmergencyStop();

        Assert.Throws<InvalidOperationException>(() => travel.LowerCarrier(Transfer, heightMm: 120));
    }

    [Fact]
    public void キャリアがすでに下がっていたら下げられない()
    {
        var travel = LoweredAtTransfer();

        Assert.Throws<InvalidOperationException>(() => travel.LowerCarrier(Transfer, heightMm: 120));
    }

    [Fact]
    public void 下がった通知でキャリアが下がった状態になる()
    {
        var travel = IdleAtTransfer();
        travel.LowerCarrier(Transfer, heightMm: 120);

        var outcome = travel.OnCarrierLowered(2);

        Assert.Equal(NotificationOutcome.Accepted, outcome);
        Assert.Equal(CarrierState.Lowered, travel.Carrier);
    }

    [Fact]
    public void 測定させたときは測定した高さの通知を受け付ける()
    {
        var travel = IdleAtTransfer();
        travel.LowerCarrier(Transfer, heightMm: null);

        var outcome = travel.OnCarrierHeightMeasured(2, heightMm: 118);

        Assert.Equal(NotificationOutcome.Accepted, outcome);
    }

    [Fact]
    public void 高さを入れて渡したときの測定した高さの通知は予期しない通知()
    {
        var travel = IdleAtTransfer();
        travel.LowerCarrier(Transfer, heightMm: 120);

        var outcome = travel.OnCarrierHeightMeasured(2, heightMm: 118);

        Assert.Equal(NotificationOutcome.IgnoredUnexpected, outcome);
    }

    [Fact]
    public void 予期しない下がった通知は無視する()
    {
        var travel = IdleAtTransfer();

        var outcome = travel.OnCarrierLowered(2);

        Assert.Equal(NotificationOutcome.IgnoredUnexpected, outcome);
        Assert.Equal(CarrierState.Raised, travel.Carrier);
    }

    // ---- 上げる ----

    [Fact]
    public void キャリアが下がっていれば上げるコマンドを用意して上がった通知で上がった状態になる()
    {
        var travel = LoweredAtTransfer();

        travel.RaiseCarrier();
        Assert.Equal(CarrierState.Raising, travel.Carrier);
        Assert.Equal([new RaiseCarrierCommand(3)], travel.OnCommandRequest(2));

        var outcome = travel.OnCarrierRaised(3);
        Assert.Equal(NotificationOutcome.Accepted, outcome);
        Assert.Equal(CarrierState.Raised, travel.Carrier);
    }

    [Fact]
    public void キャリアが上がっていれば上げられない()
    {
        var travel = IdleAtTransfer();

        Assert.Throws<InvalidOperationException>(() => travel.RaiseCarrier());
    }

    [Fact]
    public void 緊急停止中はアラームクリアするまで上げられない()
    {
        var travel = LoweredAtTransfer();
        travel.EmergencyStop();

        Assert.Throws<InvalidOperationException>(() => travel.RaiseCarrier());
    }

    [Fact]
    public void 予期しない上がった通知は無視する()
    {
        var travel = LoweredAtTransfer();

        var outcome = travel.OnCarrierRaised(3);

        Assert.Equal(NotificationOutcome.IgnoredUnexpected, outcome);
        Assert.Equal(CarrierState.Lowered, travel.Carrier);
    }

    [Fact]
    public void 異常のときは位置リセットのあとに上げる()
    {
        var travel = LoweredAtTransfer();
        travel.OnAgvError(AgvErrorKind.CrossMissed); // 異常
        Assert.Throws<InvalidOperationException>(() => travel.RaiseCarrier());

        travel.ResetPosition(new Pose("T", 0)); // 番号 3
        travel.OnCommandRequest(2);
        travel.OnPositionResetCompleted(3);

        Assert.Equal(CarrierState.Lowered, travel.Carrier); // 位置リセットでもキャリアの状態は保つ
        travel.RaiseCarrier();
        Assert.Equal(CarrierState.Raising, travel.Carrier);
    }

    // ---- 走行との関係 ----

    [Fact]
    public void キャリアが下がっている間は走らせない()
    {
        var travel = LoweredAtTransfer();
        var route = TravelSectionBuilder.Build(
            new Pose("T", 0),
            [new MoveStep(MoveDirection.Backward, "T", "A", 800, new Pose("A", 0))]);

        Assert.Throws<InvalidOperationException>(() => travel.SetRoute(route));
    }

    // ---- 緊急停止とエラー ----

    [Fact]
    public void 緊急停止するとまだ渡していないキャリアのコマンドを取り下げてアラームクリアのあとに渡し直す()
    {
        var travel = IdleAtTransfer();
        travel.LowerCarrier(Transfer, heightMm: 120); // 番号 2、まだ渡していない

        travel.EmergencyStop(); // 番号 3
        Assert.Equal(CarrierState.Raised, travel.Carrier);
        Assert.Equal([new EmergencyStopCommand(3)], travel.OnCommandRequest(1));

        travel.ClearAlarm(); // 番号 4
        travel.OnCommandRequest(3);
        travel.OnAlarmCleared(4);

        Assert.Equal(CarrierState.Lowering, travel.Carrier);
        Assert.Equal([new LowerCarrierCommand(5, 120)], travel.OnCommandRequest(4));
    }

    [Fact]
    public void エラーのときもまだ渡していないキャリアのコマンドを取り下げてアラームクリアのあとに渡し直す()
    {
        var travel = LoweredAtTransfer();
        travel.RaiseCarrier(); // 番号 3、まだ渡していない

        travel.OnAgvError(AgvErrorKind.BumperPressed);
        Assert.Equal(CarrierState.Lowered, travel.Carrier);
        Assert.Empty(travel.OnCommandRequest(2));

        travel.ClearAlarm(); // 番号 4
        travel.OnCommandRequest(2);
        travel.OnAlarmCleared(4);

        Assert.Equal([new RaiseCarrierCommand(5)], travel.OnCommandRequest(4));
    }

    [Fact]
    public void 上げ下げの途中で止まってもアラームクリアのあとに届いた完了の通知を受け付ける()
    {
        var travel = IdleAtTransfer();
        travel.LowerCarrier(Transfer, heightMm: 120);
        travel.OnCommandRequest(2); // AGV が受け取って下げ始めた
        travel.OnAgvError(AgvErrorKind.MotorDriverError); // 途中で止まった
        travel.ClearAlarm(); // 番号 3
        travel.OnCommandRequest(2);
        travel.OnAlarmCleared(3); // 途中から再開

        var outcome = travel.OnCarrierLowered(2);

        Assert.Equal(NotificationOutcome.Accepted, outcome);
        Assert.Equal(CarrierState.Lowered, travel.Carrier);
    }

    // ---- 位置リセットの条件 ----

    [Fact]
    public void 走行中は位置リセットを受け付けない()
    {
        var travel = new AgvTravel(Agv1, new PanelReservations());
        travel.ResetPosition(new Pose("A", 0));
        travel.OnCommandRequest(NoneReceived);
        travel.OnPositionResetCompleted(1);
        travel.SetRoute(TravelSectionBuilder.Build(
            new Pose("A", 0),
            [new MoveStep(MoveDirection.Forward, "A", "B", 800, new Pose("B", 0))]));
        travel.OnCommandRequest(1); // AGV に渡した

        Assert.Throws<InvalidOperationException>(() => travel.ResetPosition(new Pose("A", 0)));
    }

    [Fact]
    public void まだ渡していない動作コマンドだけなら位置リセットできる()
    {
        var travel = new AgvTravel(Agv1, new PanelReservations());
        travel.ResetPosition(new Pose("A", 0));
        travel.OnCommandRequest(NoneReceived);
        travel.OnPositionResetCompleted(1);
        travel.SetRoute(TravelSectionBuilder.Build(
            new Pose("A", 0),
            [new MoveStep(MoveDirection.Forward, "A", "B", 800, new Pose("B", 0))]));

        var result = travel.ResetPosition(new Pose("A", 0));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void キャリアの上げ下げ中は位置リセットを受け付けない()
    {
        var travel = IdleAtTransfer();
        travel.LowerCarrier(Transfer, heightMm: 120);
        travel.OnCommandRequest(1); // AGV に渡した

        Assert.Throws<InvalidOperationException>(() => travel.ResetPosition(new Pose("T", 0)));
    }

    [Fact]
    public void 上げ下げの途中で緊急停止して位置リセットするとキャリアが下がっているとして扱う()
    {
        var travel = IdleAtTransfer();
        travel.LowerCarrier(Transfer, heightMm: 120);
        travel.OnCommandRequest(1);
        travel.EmergencyStop(); // 番号 3
        travel.OnCommandRequest(2);

        travel.ResetPosition(new Pose("T", 0)); // 番号 4

        Assert.Equal(CarrierState.Lowered, travel.Carrier);
        Assert.Equal(NotificationOutcome.IgnoredUnexpected, travel.OnCarrierLowered(2)); // AGV は指示を破棄した
    }
}
