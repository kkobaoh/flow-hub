using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic.Domain;

namespace FlowHub.Traffic.Tests;

public class AgvTravelTests
{
    private static readonly AgvId Agv1 = new("AGV-1");
    private static readonly AgvId Agv2 = new("AGV-2");

    private const long NoneReceived = -1;

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

    /// <summary>
    /// 位置リセットを AGV に渡し、完了の通知まで受けて、待機中にする。位置リセットの番号は 1。
    /// </summary>
    private static (AgvTravel Travel, PanelReservations Reservations) IdleAt(Pose pose)
    {
        var reservations = new PanelReservations();
        var travel = new AgvTravel(Agv1, reservations);
        travel.ResetPosition(pose);
        travel.OnCommandRequest(NoneReceived);
        travel.OnPositionResetCompleted(1);
        return (travel, reservations);
    }

    /// <summary>
    /// A から D へ出発し、最初の動作コマンド（番号 2）を AGV に渡した状態。
    /// </summary>
    private static (AgvTravel Travel, PanelReservations Reservations) MovingAlongRouteAtoD()
    {
        var (travel, reservations) = IdleAt(new Pose("A", 0));
        travel.StartRoute(RouteAtoD());
        travel.OnCommandRequest(1);
        return (travel, reservations);
    }

    private static MotionCommand Motion(long number, TravelSection section) => new(number, section.Commands);

    // ---- 位置リセット ----

    [Fact]
    public void 登録したばかりのAGVは位置がわからない()
    {
        var travel = new AgvTravel(Agv1, new PanelReservations());

        Assert.Equal(AgvTravelStatus.Unregistered, travel.Status);
        Assert.Null(travel.Position);
    }

    [Fact]
    public void 位置リセットでパネルを予約し位置リセットのコマンドを番号1で用意する()
    {
        var reservations = new PanelReservations();
        var travel = new AgvTravel(Agv1, reservations);

        var result = travel.ResetPosition(new Pose("A", 90));

        Assert.True(result.Succeeded);
        Assert.Equal(AgvTravelStatus.ResettingPosition, travel.Status);
        Assert.Equal(new Pose("A", 90), travel.Position);
        Assert.Equal(Agv1, reservations.OwnerOf(P("A")));
        Assert.Equal([new PositionResetCommand(1, new Pose("A", 90))], travel.OnCommandRequest(NoneReceived));
    }

    [Fact]
    public void ほかのAGVが予約しているパネルには位置リセットできない()
    {
        var reservations = new PanelReservations();
        reservations.TryReserve(Agv2, [P("A")]);
        var travel = new AgvTravel(Agv1, reservations);

        var result = travel.ResetPosition(new Pose("A", 0));

        Assert.False(result.Succeeded);
        Assert.Equal(AgvTravelStatus.Unregistered, travel.Status);
        Assert.Empty(travel.OnCommandRequest(NoneReceived));
    }

    [Fact]
    public void 位置リセットの完了の通知を受けると待機中になる()
    {
        var travel = new AgvTravel(Agv1, new PanelReservations());
        travel.ResetPosition(new Pose("A", 0));
        travel.OnCommandRequest(NoneReceived);

        var outcome = travel.OnPositionResetCompleted(1);

        Assert.Equal(NotificationOutcome.Accepted, outcome);
        Assert.Equal(AgvTravelStatus.Idle, travel.Status);
    }

    [Fact]
    public void 位置リセットの完了を待っている間は出発できない()
    {
        var travel = new AgvTravel(Agv1, new PanelReservations());
        travel.ResetPosition(new Pose("A", 0));

        Assert.Throws<InvalidOperationException>(() => travel.StartRoute(RouteAtoD()));
    }

    [Fact]
    public void 予期しない位置リセットの完了の通知は無視して警告する()
    {
        var (travel, _) = IdleAt(new Pose("A", 0));

        var outcome = travel.OnPositionResetCompleted(1);

        Assert.Equal(NotificationOutcome.IgnoredUnexpected, outcome);
        Assert.Equal(AgvTravelStatus.Idle, travel.Status);
    }

    [Fact]
    public void 位置リセットすると残りの経路を捨ててほかの予約を解放する()
    {
        var (travel, reservations) = MovingAlongRouteAtoD();

        travel.ResetPosition(new Pose("B", 180));

        Assert.Equal(AgvTravelStatus.ResettingPosition, travel.Status);
        Assert.Equal([P("B")], reservations.ReservedBy(Agv1));
    }

    [Fact]
    public void 位置リセットするとまだ渡していない動作コマンドを取り下げる()
    {
        var (travel, _) = IdleAt(new Pose("A", 0));
        travel.StartRoute(RouteAtoD()); // 動作コマンド 2 を用意したが、まだ渡していない

        travel.ResetPosition(new Pose("A", 0));

        Assert.Equal([new PositionResetCommand(3, new Pose("A", 0))], travel.OnCommandRequest(1));
    }

    // ---- コマンドの受け渡し ----

    [Fact]
    public void 番号は1から1つずつ増やして付ける()
    {
        var (travel, _) = IdleAt(new Pose("A", 0));
        var route = RouteAtoD();

        travel.StartRoute(route);

        Assert.Equal([Motion(2, route[0])], travel.OnCommandRequest(1));
    }

    [Fact]
    public void AGVがまだ受け取っていなければもう一度渡す()
    {
        var (travel, _) = IdleAt(new Pose("A", 0));
        var route = RouteAtoD();
        travel.StartRoute(route);
        travel.OnCommandRequest(1); // 渡したが、レスポンスが失われた

        var commands = travel.OnCommandRequest(1);

        Assert.Equal([Motion(2, route[0])], commands);
    }

    [Fact]
    public void AGVが受け取っていればもう一度は渡さない()
    {
        var (travel, _) = MovingAlongRouteAtoD();

        var commands = travel.OnCommandRequest(2);

        Assert.Empty(commands);
    }

    [Fact]
    public void 受信の完了の通知はログに残すだけ()
    {
        var (travel, _) = MovingAlongRouteAtoD();

        var outcome = travel.OnMotionReceived(2);

        Assert.Equal(NotificationOutcome.Logged, outcome);
        Assert.Equal(AgvTravelStatus.Moving, travel.Status);
    }

    [Fact]
    public void 再起動したAGVには走りかけの動作コマンドを渡し直さず位置リセットを待つ()
    {
        var (travel, reservations) = MovingAlongRouteAtoD();

        var commands = travel.OnCommandRequest(NoneReceived);

        Assert.Empty(commands);
        Assert.Equal(AgvTravelStatus.Error, travel.Status);
        Assert.Equal(4, reservations.ReservedBy(Agv1).Count); // 予約は保つ
    }

    [Fact]
    public void 再起動したAGVにはまだ渡していない位置リセットだけを渡す()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        travel.EmergencyStop(); // 番号 3
        travel.OnCommandRequest(NoneReceived); // 再起動した
        travel.ResetPosition(new Pose("B", 0)); // 番号 4

        var commands = travel.OnCommandRequest(NoneReceived);

        Assert.Equal([new PositionResetCommand(4, new Pose("B", 0))], commands);
    }

    [Fact]
    public void 位置を取得するリクエストには最後に把握していた位置を返す()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        travel.OnMoveCompleted(2, MoveDirection.Forward, P("B"));

        Assert.Equal(new Pose("B", 0), travel.OnPositionRequest());
    }

    // ---- 出発と走行 ----

    [Fact]
    public void 出発するときは経路全体のパネルをまとめて予約し最初の区間の動作コマンドを用意する()
    {
        var (travel, reservations) = IdleAt(new Pose("A", 0));
        var route = RouteAtoD();

        var result = travel.StartRoute(route);

        Assert.True(result.Succeeded);
        Assert.Equal(AgvTravelStatus.Moving, travel.Status);
        Assert.Equal(["A", "B", "C", "D"], reservations.ReservedBy(Agv1).Select(p => p.Value).Order(StringComparer.Ordinal));
        Assert.Equal([Motion(2, route[0])], travel.OnCommandRequest(1));
    }

    [Fact]
    public void 経路の一部でもほかのAGVが予約していたら出発しない()
    {
        var (travel, reservations) = IdleAt(new Pose("A", 0));
        reservations.TryReserve(Agv2, [P("D")]);

        var result = travel.StartRoute(RouteAtoD());

        Assert.False(result.Succeeded);
        Assert.Equal([P("D")], result.Reservation.Conflicts);
        Assert.Equal(AgvTravelStatus.Idle, travel.Status);
        Assert.Empty(travel.OnCommandRequest(1));
    }

    [Fact]
    public void 待機中でなければ出発できない()
    {
        var (travel, _) = MovingAlongRouteAtoD();

        Assert.Throws<InvalidOperationException>(() => travel.StartRoute(RouteAtoD()));
    }

    [Fact]
    public void 今いるパネルから始まらない経路では出発できない()
    {
        var (travel, _) = IdleAt(new Pose("B", 0));

        Assert.Throws<ArgumentException>(() => travel.StartRoute(RouteAtoD()));
    }

    [Fact]
    public void 前進の完了で位置を更新し後ろのパネルを解放する()
    {
        var (travel, reservations) = MovingAlongRouteAtoD();

        var outcome = travel.OnMoveCompleted(2, MoveDirection.Forward, P("B"));

        Assert.Equal(NotificationOutcome.Accepted, outcome);
        Assert.Equal(new Pose("B", 0), travel.Position);
        Assert.Null(reservations.OwnerOf(P("A")));
    }

    [Fact]
    public void あとでまた通るパネルは解放しない()
    {
        // A → B → A → C（A をあとでまた通る）
        var (travel, reservations) = IdleAt(new Pose("A", 0));
        travel.StartRoute(TravelSectionBuilder.Build(
            new Pose("A", 0),
            [
                Forward("A", "B", 0),
                new MoveStep(MoveDirection.Backward, "B", "A", 800, new Pose("A", 0)),
                new MoveStep(MoveDirection.Backward, "A", "C", 800, new Pose("C", 0)),
            ]));
        travel.OnCommandRequest(1);

        travel.OnMoveCompleted(2, MoveDirection.Forward, P("B"));

        Assert.Equal(Agv1, reservations.OwnerOf(P("A")));
    }

    [Fact]
    public void 回転の完了では角度を照合せず向きは指示した角度にする()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        travel.OnMoveCompleted(2, MoveDirection.Forward, P("B"));
        travel.OnMoveCompleted(2, MoveDirection.Forward, P("C"));

        var outcome = travel.OnRotationCompleted(2, reportedHeading: 88);

        Assert.Equal(NotificationOutcome.Accepted, outcome);
        Assert.Equal(new Pose("C", 90), travel.Position);
    }

    [Fact]
    public void 区間の最後の動作を終えたら次の区間の動作コマンドを用意する()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        var route = RouteAtoD();
        travel.OnMoveCompleted(2, MoveDirection.Forward, P("B"));
        travel.OnMoveCompleted(2, MoveDirection.Forward, P("C"));

        travel.OnRotationCompleted(2, reportedHeading: 90);

        Assert.Equal([Motion(3, route[1])], travel.OnCommandRequest(2));
    }

    [Fact]
    public void 区間の途中では次の区間を用意しない()
    {
        var (travel, _) = MovingAlongRouteAtoD();

        travel.OnMoveCompleted(2, MoveDirection.Forward, P("B"));

        Assert.Empty(travel.OnCommandRequest(2));
    }

    [Fact]
    public void 経路を走り終えたら待機中になり今いるパネルだけを予約している()
    {
        var (travel, reservations) = MovingAlongRouteAtoD();
        travel.OnMoveCompleted(2, MoveDirection.Forward, P("B"));
        travel.OnMoveCompleted(2, MoveDirection.Forward, P("C"));
        travel.OnRotationCompleted(2, reportedHeading: 90);
        travel.OnCommandRequest(2);

        var outcome = travel.OnMoveCompleted(3, MoveDirection.Forward, P("D"));

        Assert.Equal(NotificationOutcome.Accepted, outcome);
        Assert.Equal(AgvTravelStatus.Idle, travel.Status);
        Assert.Equal([P("D")], reservations.ReservedBy(Agv1));
    }

    // ---- 予期しない通知 ----

    [Fact]
    public void 直前に受け付けた通知と同じ通知は重複として無視する()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        travel.OnMoveCompleted(2, MoveDirection.Forward, P("B"));

        var outcome = travel.OnMoveCompleted(2, MoveDirection.Forward, P("B"));

        Assert.Equal(NotificationOutcome.IgnoredDuplicate, outcome);
        Assert.Equal(new Pose("B", 0), travel.Position);
        Assert.Equal(AgvTravelStatus.Moving, travel.Status);
    }

    [Fact]
    public void 走り終えた動作コマンドの完了の通知は遅れて届いた重複として無視する()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        travel.OnMoveCompleted(2, MoveDirection.Forward, P("B"));
        travel.OnMoveCompleted(2, MoveDirection.Forward, P("C"));
        travel.OnRotationCompleted(2, reportedHeading: 90);
        travel.OnCommandRequest(2);

        var outcome = travel.OnMoveCompleted(2, MoveDirection.Forward, P("B"));

        Assert.Equal(NotificationOutcome.IgnoredDuplicate, outcome);
        Assert.Equal(AgvTravelStatus.Moving, travel.Status);
    }

    [Fact]
    public void 位置リセットで捨てた動作コマンドの通知は古い通知として無視する()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        travel.ResetPosition(new Pose("A", 0)); // 番号 3
        travel.OnCommandRequest(2);
        travel.OnPositionResetCompleted(3);

        var outcome = travel.OnMoveCompleted(2, MoveDirection.Forward, P("B"));

        Assert.Equal(NotificationOutcome.IgnoredStale, outcome);
        Assert.Equal(new Pose("A", 0), travel.Position);
        Assert.Equal(AgvTravelStatus.Idle, travel.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void 送っていない番号の通知は受け付けない(long number)
    {
        var (travel, _) = MovingAlongRouteAtoD();

        var outcome = travel.OnMoveCompleted(number, MoveDirection.Forward, P("B"));

        Assert.Equal(NotificationOutcome.Rejected, outcome);
        Assert.Equal(AgvTravelStatus.Moving, travel.Status);
    }

    [Fact]
    public void 動作コマンドでない番号の完了の通知は受け付けない()
    {
        var (travel, _) = MovingAlongRouteAtoD();

        var outcome = travel.OnMoveCompleted(1, MoveDirection.Forward, P("B")); // 1 は位置リセット

        Assert.Equal(NotificationOutcome.Rejected, outcome);
    }

    [Fact]
    public void 指示と食い違う通知は緊急停止を送って異常にし予約を保つ()
    {
        var (travel, reservations) = MovingAlongRouteAtoD();

        var outcome = travel.OnMoveCompleted(2, MoveDirection.Backward, P("B"));

        Assert.Equal(NotificationOutcome.Fault, outcome);
        Assert.Equal(AgvTravelStatus.Error, travel.Status);
        Assert.Equal(new Pose("A", 0), travel.Position);
        Assert.Equal(4, reservations.ReservedBy(Agv1).Count);
        Assert.Equal([new EmergencyStopCommand(3)], travel.OnCommandRequest(2));
    }

    [Fact]
    public void 手前の動作を飛ばした通知は異常にする()
    {
        var (travel, _) = MovingAlongRouteAtoD();

        var outcome = travel.OnMoveCompleted(2, MoveDirection.Forward, P("C")); // B を飛ばした

        Assert.Equal(NotificationOutcome.Fault, outcome);
        Assert.Equal(AgvTravelStatus.Error, travel.Status);
    }

    [Fact]
    public void 回転を待っているときの移動の完了は異常にする()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        travel.OnMoveCompleted(2, MoveDirection.Forward, P("B"));
        travel.OnMoveCompleted(2, MoveDirection.Forward, P("C"));

        var outcome = travel.OnMoveCompleted(2, MoveDirection.Forward, P("D"));

        Assert.Equal(NotificationOutcome.Fault, outcome);
    }

    [Fact]
    public void 異常のときも中身の合った通知は受け付けて位置を更新するが異常のまま()
    {
        var (travel, reservations) = MovingAlongRouteAtoD();
        travel.OnMoveCompleted(2, MoveDirection.Backward, P("B")); // 食い違い → 異常

        var outcome = travel.OnMoveCompleted(2, MoveDirection.Forward, P("B"));

        Assert.Equal(NotificationOutcome.Accepted, outcome);
        Assert.Equal(new Pose("B", 0), travel.Position);
        Assert.Equal(AgvTravelStatus.Error, travel.Status);
        Assert.Equal(Agv1, reservations.OwnerOf(P("A"))); // 異常のときは予約を解放しない
    }

    [Fact]
    public void 異常のときに先の動作の通知が中身の合った形で届いたら手前も完了したとみなす()
    {
        // B への前進の通知が食い違って異常になったあと、C への前進の通知が届いた
        var (travel, _) = MovingAlongRouteAtoD();
        travel.OnMoveCompleted(2, MoveDirection.Backward, P("B"));

        var outcome = travel.OnMoveCompleted(2, MoveDirection.Forward, P("C"));

        Assert.Equal(NotificationOutcome.Accepted, outcome);
        Assert.Equal(new Pose("C", 0), travel.Position);
        Assert.Equal(AgvTravelStatus.Error, travel.Status);
    }

    [Fact]
    public void 異常のときに中身の合わない通知が届いても緊急停止はもう一度送らない()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        travel.OnMoveCompleted(2, MoveDirection.Backward, P("B"));
        travel.OnCommandRequest(2);

        var outcome = travel.OnMoveCompleted(2, MoveDirection.Backward, P("D"));

        Assert.Equal(NotificationOutcome.IgnoredUnexpected, outcome);
        Assert.Empty(travel.OnCommandRequest(3));
    }

    [Fact]
    public void 異常のあとは位置リセットで戻れる()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        travel.OnMoveCompleted(2, MoveDirection.Backward, P("B"));

        travel.ResetPosition(new Pose("B", 0));

        Assert.Equal(AgvTravelStatus.ResettingPosition, travel.Status);
    }

    // ---- 緊急停止とアラームクリア ----

    [Fact]
    public void 緊急停止しても予約は保つ()
    {
        var (travel, reservations) = MovingAlongRouteAtoD();

        travel.EmergencyStop();

        Assert.Equal(AgvTravelStatus.EmergencyStopped, travel.Status);
        Assert.Equal(4, reservations.ReservedBy(Agv1).Count);
        Assert.Equal([new EmergencyStopCommand(3)], travel.OnCommandRequest(2));
    }

    [Fact]
    public void 緊急停止するとまだ渡していない動作コマンドを取り下げてアラームクリアのあとに渡し直す()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        var route = RouteAtoD();
        travel.OnMoveCompleted(2, MoveDirection.Forward, P("B"));
        travel.OnMoveCompleted(2, MoveDirection.Forward, P("C"));
        travel.OnRotationCompleted(2, reportedHeading: 90); // 動作コマンド 3 を用意したが、まだ渡していない

        travel.EmergencyStop(); // 番号 4
        Assert.Equal([new EmergencyStopCommand(4)], travel.OnCommandRequest(2));

        travel.ClearAlarm(); // 番号 5
        travel.OnCommandRequest(4);
        travel.OnAlarmCleared(5);

        Assert.Equal([Motion(6, route[1])], travel.OnCommandRequest(5));
    }

    [Fact]
    public void 渡したばかりの動作コマンドはAGVが受け取っていなかったとわかれば取り下げる()
    {
        var (travel, _) = IdleAt(new Pose("A", 0));
        travel.StartRoute(RouteAtoD());
        travel.OnCommandRequest(1); // 動作コマンド 2 を渡したが、受け取ったかはまだわからない

        travel.EmergencyStop(); // 番号 3

        Assert.Equal([new EmergencyStopCommand(3)], travel.OnCommandRequest(1)); // 2 は受け取っていなかった
    }

    [Fact]
    public void 渡したばかりの動作コマンドをAGVが受け取っていたら取り下げない()
    {
        var (travel, _) = IdleAt(new Pose("A", 0));
        var route = RouteAtoD();
        travel.StartRoute(route);
        travel.OnCommandRequest(1);
        travel.EmergencyStop(); // 番号 3

        travel.OnCommandRequest(2); // 2 は受け取っていた

        var outcome = travel.OnMoveCompleted(2, MoveDirection.Forward, P("B"));
        Assert.Equal(NotificationOutcome.Accepted, outcome);
    }

    [Fact]
    public void アラームクリアの完了の通知を受けるまでは緊急停止中のまま()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        travel.EmergencyStop();

        travel.ClearAlarm();

        Assert.Equal(AgvTravelStatus.EmergencyStopped, travel.Status);
        Assert.Equal([new EmergencyStopCommand(3), new AlarmClearCommand(4)], travel.OnCommandRequest(2));
    }

    [Fact]
    public void アラームクリアの完了の通知で走行中に戻る()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        travel.EmergencyStop();
        travel.ClearAlarm();

        var outcome = travel.OnAlarmCleared(4);

        Assert.Equal(NotificationOutcome.Accepted, outcome);
        Assert.Equal(AgvTravelStatus.Moving, travel.Status);
    }

    [Fact]
    public void 緊急停止中に区間を終えても次の区間はアラームクリアの完了の通知を受けるまで用意しない()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        var route = RouteAtoD();
        travel.OnMoveCompleted(2, MoveDirection.Forward, P("B"));
        travel.OnMoveCompleted(2, MoveDirection.Forward, P("C"));
        travel.EmergencyStop(); // 番号 3
        travel.OnCommandRequest(2);

        travel.OnRotationCompleted(2, reportedHeading: 90);
        Assert.Empty(travel.OnCommandRequest(3));

        travel.ClearAlarm(); // 番号 4
        travel.OnCommandRequest(3);
        travel.OnAlarmCleared(4);
        Assert.Equal([Motion(5, route[1])], travel.OnCommandRequest(4));
    }

    [Fact]
    public void 予期しないアラームクリアの完了の通知は無視して警告する()
    {
        var (travel, _) = MovingAlongRouteAtoD();

        var outcome = travel.OnAlarmCleared(2);

        Assert.Equal(NotificationOutcome.IgnoredUnexpected, outcome);
        Assert.Equal(AgvTravelStatus.Moving, travel.Status);
    }

    [Fact]
    public void 待機中に緊急停止してアラームクリアすると待機中に戻る()
    {
        var (travel, _) = IdleAt(new Pose("A", 0));
        travel.EmergencyStop();
        travel.ClearAlarm();

        travel.OnAlarmCleared(3);

        Assert.Equal(AgvTravelStatus.Idle, travel.Status);
    }

    [Fact]
    public void 緊急停止していなければアラームクリアできない()
    {
        var (travel, _) = MovingAlongRouteAtoD();

        Assert.Throws<InvalidOperationException>(() => travel.ClearAlarm());
    }

    [Fact]
    public void 異常のときはアラームクリアできない()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        travel.OnMoveCompleted(2, MoveDirection.Backward, P("B"));

        Assert.Throws<InvalidOperationException>(() => travel.ClearAlarm());
    }

    [Fact]
    public void 異常のときに緊急停止しても異常のまま()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        travel.OnMoveCompleted(2, MoveDirection.Backward, P("B"));

        travel.EmergencyStop();

        Assert.Equal(AgvTravelStatus.Error, travel.Status);
    }

    [Fact]
    public void 位置がわからないAGVは緊急停止できない()
    {
        var travel = new AgvTravel(Agv1, new PanelReservations());

        Assert.Throws<InvalidOperationException>(() => travel.EmergencyStop());
    }

    // ---- AGV のエラー ----

    [Theory]
    [InlineData(nameof(AgvErrorKind.CrossMissed))]
    [InlineData(nameof(AgvErrorKind.CrossFalselyDetected))]
    public void 位置がずれた可能性のあるエラーは異常にする(string kindName)
    {
        var (travel, reservations) = MovingAlongRouteAtoD();

        var outcome = travel.OnAgvError(Enum.Parse<AgvErrorKind>(kindName));

        Assert.Equal(NotificationOutcome.Accepted, outcome);
        Assert.Equal(AgvTravelStatus.Error, travel.Status);
        Assert.Equal(4, reservations.ReservedBy(Agv1).Count);
        Assert.Empty(travel.OnCommandRequest(2)); // AGV は止まっているので、緊急停止は送らない
    }

    [Theory]
    [InlineData(nameof(AgvErrorKind.BumperPressed))]
    [InlineData(nameof(AgvErrorKind.CommunicationLost))]
    [InlineData(nameof(AgvErrorKind.BatteryDepleted))]
    [InlineData(nameof(AgvErrorKind.MotorDriverError))]
    [InlineData(nameof(AgvErrorKind.HardwareError))]
    [InlineData(nameof(AgvErrorKind.ServerRequestedStop))]
    public void 位置はずれていないエラーは緊急停止中にする(string kindName)
    {
        var (travel, _) = MovingAlongRouteAtoD();

        travel.OnAgvError(Enum.Parse<AgvErrorKind>(kindName));

        Assert.Equal(AgvTravelStatus.EmergencyStopped, travel.Status);
    }

    [Fact]
    public void エラーのときもまだ渡していない動作コマンドを取り下げてアラームクリアのあとに渡し直す()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        var route = RouteAtoD();
        travel.OnMoveCompleted(2, MoveDirection.Forward, P("B"));
        travel.OnMoveCompleted(2, MoveDirection.Forward, P("C"));
        travel.OnRotationCompleted(2, reportedHeading: 90); // 動作コマンド 3 を用意したが、まだ渡していない

        travel.OnAgvError(AgvErrorKind.BumperPressed);
        Assert.Empty(travel.OnCommandRequest(2));

        travel.ClearAlarm(); // 番号 4
        travel.OnCommandRequest(2);
        travel.OnAlarmCleared(4);
        Assert.Equal([Motion(5, route[1])], travel.OnCommandRequest(4));
    }

    [Fact]
    public void 異常のときに位置はずれていないエラーが届いても異常のまま()
    {
        var (travel, _) = MovingAlongRouteAtoD();
        travel.OnMoveCompleted(2, MoveDirection.Backward, P("B"));

        travel.OnAgvError(AgvErrorKind.BumperPressed);

        Assert.Equal(AgvTravelStatus.Error, travel.Status);
    }

    [Fact]
    public void 位置がわからないAGVのエラーはログに残すだけ()
    {
        var travel = new AgvTravel(Agv1, new PanelReservations());

        var outcome = travel.OnAgvError(AgvErrorKind.BumperPressed);

        Assert.Equal(NotificationOutcome.Logged, outcome);
        Assert.Equal(AgvTravelStatus.Unregistered, travel.Status);
    }
}
