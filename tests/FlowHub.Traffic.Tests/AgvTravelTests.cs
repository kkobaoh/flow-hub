using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic.Domain;

namespace FlowHub.Traffic.Tests;

public class AgvTravelTests
{
    private static readonly AgvId Agv1 = new("AGV-1");
    private static readonly AgvId Agv2 = new("AGV-2");

    private static PanelId P(string id) => new(id);

    private static MoveStep Forward(string from, string to, int heading) =>
        new(MoveDirection.Forward, from, to, 800, new Pose(to, heading));

    private static RotateStep Rotate(string at, int heading) => new(at, heading, new Pose(at, heading));

    /// <summary>
    /// A →(0度) B →(0度) C で 90度へ回転し、C →(90度) D。区間は [A→B→C, 回転] と [C→D] の2つ。
    /// </summary>
    private static IReadOnlyList<TravelSection> RouteAtoD() =>
        TravelSectionBuilder.Build(
            new Pose("A", 0),
            [Forward("A", "B", 0), Forward("B", "C", 0), Rotate("C", 90), Forward("C", "D", 90)]);

    private static (AgvTravel Travel, PanelReservations Reservations) RegisteredAt(Pose pose)
    {
        var reservations = new PanelReservations();
        var travel = new AgvTravel(Agv1, reservations);
        travel.ResetPosition(pose);
        return (travel, reservations);
    }

    private static (AgvTravel Travel, PanelReservations Reservations) MovingAlongRouteAtoD()
    {
        var (travel, reservations) = RegisteredAt(new Pose("A", 0));
        travel.StartRoute(RouteAtoD());
        return (travel, reservations);
    }

    // ---- 位置リセット ----

    [Fact]
    public void 登録したばかりのAGVは位置がわからない()
    {
        var travel = new AgvTravel(Agv1, new PanelReservations());

        Assert.Equal(AgvTravelStatus.Unregistered, travel.Status);
        Assert.Null(travel.Position);
    }

    [Fact]
    public void 位置リセットでパネルを予約しAGVに位置を送る()
    {
        var reservations = new PanelReservations();
        var travel = new AgvTravel(Agv1, reservations);

        var result = travel.ResetPosition(new Pose("A", 90));

        Assert.True(result.Succeeded);
        Assert.Equal(new SendPositionReset(new Pose("A", 90)), result.Instruction);
        Assert.Equal(new Pose("A", 90), travel.Position);
        Assert.Equal(AgvTravelStatus.Idle, travel.Status);
        Assert.Equal(Agv1, reservations.OwnerOf(P("A")));
    }

    [Fact]
    public void ほかのAGVが予約しているパネルには位置リセットできない()
    {
        var reservations = new PanelReservations();
        reservations.TryReserve(Agv2, [P("A")]);
        var travel = new AgvTravel(Agv1, reservations);

        var result = travel.ResetPosition(new Pose("A", 0));

        Assert.False(result.Succeeded);
        Assert.Null(result.Instruction);
        Assert.Equal(AgvTravelStatus.Unregistered, travel.Status);
        Assert.Equal(Agv2, reservations.OwnerOf(P("A")));
    }

    [Fact]
    public void 位置リセットすると残りの指示を捨ててほかの予約を解放する()
    {
        var (travel, reservations) = MovingAlongRouteAtoD();

        travel.ResetPosition(new Pose("B", 180));

        Assert.Equal(AgvTravelStatus.Idle, travel.Status);
        Assert.Empty(travel.PendingCommands);
        Assert.Equal([P("B")], reservations.ReservedBy(Agv1));
    }

    // ---- 出発 ----

    [Fact]
    public void 出発するときは経路全体のパネルをまとめて予約し最初の区間を送る()
    {
        var (travel, reservations) = RegisteredAt(new Pose("A", 0));
        var route = RouteAtoD();

        var result = travel.StartRoute(route);

        Assert.True(result.Succeeded);
        Assert.Equal(new SendTravelCommands(route[0].Commands), result.Instruction);
        Assert.Equal(AgvTravelStatus.Moving, travel.Status);
        Assert.Equal(["A", "B", "C", "D"], reservations.ReservedBy(Agv1).Select(p => p.Value).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void 経路の一部でもほかのAGVが予約していたら出発しない()
    {
        var (travel, reservations) = RegisteredAt(new Pose("A", 0));
        reservations.TryReserve(Agv2, [P("D")]);

        var result = travel.StartRoute(RouteAtoD());

        Assert.False(result.Succeeded);
        Assert.Equal([P("D")], result.Reservation.Conflicts);
        Assert.Null(result.Instruction);
        Assert.Equal(AgvTravelStatus.Idle, travel.Status);
        Assert.Equal([P("A")], reservations.ReservedBy(Agv1));
    }

    [Fact]
    public void 待機中でなければ出発できない()
    {
        var (travel, _) = MovingAlongRouteAtoD();

        Assert.Throws<InvalidOperationException>(() => travel.StartRoute(RouteAtoD()));
    }

    [Fact]
    public void 位置がわからないAGVは出発できない()
    {
        var travel = new AgvTravel(Agv1, new PanelReservations());

        Assert.Throws<InvalidOperationException>(() => travel.StartRoute(RouteAtoD()));
    }

    [Fact]
    public void 今いるパネルから始まらない経路では出発できない()
    {
        var (travel, _) = RegisteredAt(new Pose("B", 0));

        Assert.Throws<ArgumentException>(() => travel.StartRoute(RouteAtoD()));
    }

    // ---- 完了の通知 ----

    [Fact]
    public void 前進の完了で位置を更新し後ろのパネルを解放する()
    {
        var (travel, reservations) = MovingAlongRouteAtoD();

        var result = travel.OnMoveCompleted(P("B"));

        Assert.True(result.Accepted);
        Assert.Equal(new Pose("B", 0), travel.Position);
        Assert.Null(reservations.OwnerOf(P("A")));
        Assert.Equal(Agv1, reservations.OwnerOf(P("B")));
    }

    [Fact]
    public void あとでまた通るパネルは解放しない()
    {
        // A → B → A → C（A をあとでまた通る）
        var (travel, reservations) = RegisteredAt(new Pose("A", 0));
        travel.StartRoute(TravelSectionBuilder.Build(
            new Pose("A", 0),
            [
                Forward("A", "B", 0),
                new MoveStep(MoveDirection.Backward, "B", "A", 800, new Pose("A", 0)),
                new MoveStep(MoveDirection.Backward, "A", "C", 800, new Pose("C", 0)),
            ]));

        travel.OnMoveCompleted(P("B"));

        Assert.Equal(Agv1, reservations.OwnerOf(P("A")));
    }

    [Fact]
    public void 区間の最後の回転を終えたら次の区間を送る()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        var route = RouteAtoD();
        travel.OnMoveCompleted(P("B"));
        travel.OnMoveCompleted(P("C"));

        var result = travel.OnRotationCompleted(90);

        Assert.True(result.Accepted);
        Assert.Equal(new Pose("C", 90), travel.Position);
        Assert.Equal(new SendTravelCommands(route[1].Commands), result.Instruction);
    }

    [Fact]
    public void 区間の途中では次の区間を送らない()
    {
        var (travel, _) = MovingAlongRouteAtoD();

        var result = travel.OnMoveCompleted(P("B"));

        Assert.Null(result.Instruction);
        Assert.Equal(AgvTravelStatus.Moving, travel.Status);
    }

    [Fact]
    public void 経路を走り終えたら待機中になり今いるパネルだけを予約している()
    {
        var (travel, reservations) = MovingAlongRouteAtoD();
        travel.OnMoveCompleted(P("B"));
        travel.OnMoveCompleted(P("C"));
        travel.OnRotationCompleted(90);

        var result = travel.OnMoveCompleted(P("D"));

        Assert.True(result.Accepted);
        Assert.Null(result.Instruction);
        Assert.Equal(AgvTravelStatus.Idle, travel.Status);
        Assert.Equal(new Pose("D", 90), travel.Position);
        Assert.Equal([P("D")], reservations.ReservedBy(Agv1));
    }

    [Fact]
    public void 指示と違うパネルへの到着は異常として止める()
    {
        var (travel, reservations) = MovingAlongRouteAtoD();

        var result = travel.OnMoveCompleted(P("C"));

        Assert.False(result.Accepted);
        Assert.Equal(AgvTravelStatus.Error, travel.Status);
        Assert.Equal(new Pose("A", 0), travel.Position);
        Assert.Contains(P("A"), reservations.ReservedBy(Agv1)); // 異常のときは予約を保つ
    }

    [Fact]
    public void 指示と違う角度への回転は異常として止める()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        travel.OnMoveCompleted(P("B"));
        travel.OnMoveCompleted(P("C"));

        var result = travel.OnRotationCompleted(180);

        Assert.False(result.Accepted);
        Assert.Equal(AgvTravelStatus.Error, travel.Status);
    }

    [Fact]
    public void 回転を待っているときの移動の完了は異常として止める()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        travel.OnMoveCompleted(P("B"));
        travel.OnMoveCompleted(P("C"));

        var result = travel.OnMoveCompleted(P("D"));

        Assert.False(result.Accepted);
        Assert.Equal(AgvTravelStatus.Error, travel.Status);
    }

    [Fact]
    public void 指示を出していないときの完了の通知は異常として止める()
    {
        var (travel, _) = RegisteredAt(new Pose("A", 0));

        var result = travel.OnMoveCompleted(P("B"));

        Assert.False(result.Accepted);
        Assert.Equal(AgvTravelStatus.Error, travel.Status);
    }

    [Fact]
    public void 異常のあとは位置リセットで待機中に戻る()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        travel.OnMoveCompleted(P("C"));

        travel.ResetPosition(new Pose("B", 0));

        Assert.Equal(AgvTravelStatus.Idle, travel.Status);
    }

    // ---- 緊急停止とアラームクリア ----

    [Fact]
    public void 緊急停止しても予約は保つ()
    {
        var (travel, reservations) = MovingAlongRouteAtoD();

        var instruction = travel.EmergencyStop();

        Assert.Equal(new SendEmergencyStop(), instruction);
        Assert.Equal(AgvTravelStatus.EmergencyStopped, travel.Status);
        Assert.Equal(4, reservations.ReservedBy(Agv1).Count);
    }

    [Fact]
    public void 緊急停止の直前に着いた動作の完了は受け付ける()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        travel.EmergencyStop();

        var result = travel.OnMoveCompleted(P("B"));

        Assert.True(result.Accepted);
        Assert.Equal(new Pose("B", 0), travel.Position);
        Assert.Equal(AgvTravelStatus.EmergencyStopped, travel.Status);
    }

    [Fact]
    public void アラームクリアで残りの指示の続きを走る()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        travel.EmergencyStop();

        var instructions = travel.ClearAlarm();

        Assert.Equal([new SendAlarmClear()], instructions);
        Assert.Equal(AgvTravelStatus.Moving, travel.Status);
    }

    [Fact]
    public void 緊急停止中に区間を終えたら次の区間はアラームクリアのときに送る()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        var route = RouteAtoD();
        travel.OnMoveCompleted(P("B"));
        travel.OnMoveCompleted(P("C"));
        travel.EmergencyStop();

        var completed = travel.OnRotationCompleted(90);
        var instructions = travel.ClearAlarm();

        Assert.Null(completed.Instruction);
        Assert.Equal([new SendAlarmClear(), new SendTravelCommands(route[1].Commands)], instructions);
    }

    [Fact]
    public void 異常のときも緊急停止は送れて異常のまま()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        travel.OnMoveCompleted(P("C"));

        var instruction = travel.EmergencyStop();

        Assert.Equal(new SendEmergencyStop(), instruction);
        Assert.Equal(AgvTravelStatus.Error, travel.Status);
    }

    [Fact]
    public void 異常のときはアラームクリアできず位置リセットが必要()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        travel.OnMoveCompleted(P("C"));
        travel.EmergencyStop();

        Assert.Throws<InvalidOperationException>(() => travel.ClearAlarm());
    }

    [Fact]
    public void 待機中に緊急停止してアラームクリアすると待機中に戻る()
    {
        var (travel, _) = RegisteredAt(new Pose("A", 0));
        travel.EmergencyStop();

        travel.ClearAlarm();

        Assert.Equal(AgvTravelStatus.Idle, travel.Status);
    }

    [Fact]
    public void 緊急停止していなければアラームクリアできない()
    {
        var (travel, _) = MovingAlongRouteAtoD();

        Assert.Throws<InvalidOperationException>(() => travel.ClearAlarm());
    }

    [Fact]
    public void 位置がわからないAGVは緊急停止できない()
    {
        var travel = new AgvTravel(Agv1, new PanelReservations());

        Assert.Throws<InvalidOperationException>(() => travel.EmergencyStop());
    }
}
