using FlowHub.BuildingBlocks;
using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic.Application;
using FlowHub.Traffic.Domain;

namespace FlowHub.Traffic.Tests;

/// <summary>
/// コントロールステータス（オート、割り当て停止、マニュアル、ロック、切り離し）のテスト。
/// </summary>
public class ControlStatusTests
{
    private static readonly AgvId Agv1 = new("AGV-1");
    private static readonly AgvId Agv2 = new("AGV-2");
    private static readonly MotionProfile Motion = new(500, 250, 90);
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly string[] CrossroadsPanels = ["N", "M", "S", "E", "W"];

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class IgnoringPublisher : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
            where TEvent : IIntegrationEvent => Task.CompletedTask;
    }

    /// <summary>十字路。N →(180度) M →(180度) S と、E →(270度) M →(270度) W。</summary>
    private static InMemoryMapStore Crossroads()
    {
        var store = new InMemoryMapStore();
        static PanelId P(string id) => new(id);
        store.Publish(MapSnapshot.Create(
            CrossroadsPanels.Select(id => new Panel(P(id), PanelType.Normal, false)),
            [
                new Connection(P("N"), P("M"), 800, new Heading(180), OneWay: false),
                new Connection(P("M"), P("S"), 800, new Heading(180), OneWay: false),
                new Connection(P("E"), P("M"), 800, new Heading(270), OneWay: false),
                new Connection(P("M"), P("W"), 800, new Heading(270), OneWay: false),
            ]).Snapshot!);
        return store;
    }

    private static async Task ReadyAsync(TrafficService service, AgvId agv, Pose pose)
    {
        service.RegisterAgv(agv, Motion);
        await service.ResetPositionAsync(agv, pose, None);
        await service.HandleCommandRequestAsync(agv, -1, None);
        await service.HandlePositionResetCompletedAsync(agv, 1, None);
    }

    /// <summary>AGV-1 は N（180度）、AGV-2 は十字路の真ん中 M（270度）で待機中。</summary>
    private static async Task<TrafficService> TwoAgvsAsync()
    {
        var service = new TrafficService(Crossroads(), new IgnoringPublisher(), new FixedTimeProvider());
        await ReadyAsync(service, Agv1, new Pose("N", 180));
        await ReadyAsync(service, Agv2, new Pose("M", 270));
        return service;
    }

    private static Task<bool> ChangeAsync(TrafficService service, AgvId agv, ControlStatus status, Pose? position = null) =>
        service.ChangeControlStatusAsync(agv, status, position, None);

    // ---- 切り替えの条件 ----

    [Fact]
    public async Task ステータスはAGVの様子に載る()
    {
        var service = await TwoAgvsAsync();

        await ChangeAsync(service, Agv2, ControlStatus.AssignmentPaused);

        Assert.Equal(ControlStatus.Auto, service.GetAgv(Agv1).Control);
        Assert.Equal(ControlStatus.AssignmentPaused, service.GetAgv(Agv2).Control);
    }

    [Fact]
    public async Task 割り当て停止とオートには走っている間でも切り替えられる()
    {
        var service = await TwoAgvsAsync();
        await service.MoveAgvAsync(Agv1, new PanelId("M"), null, "move", None); // M は AGV-2 がいるが、まずは走らせる

        await service.MoveAgvAsync(Agv2, new PanelId("W"), null, "move", None);
        await ChangeAsync(service, Agv2, ControlStatus.AssignmentPaused);
        await ChangeAsync(service, Agv2, ControlStatus.Auto);

        Assert.Equal(ControlStatus.Auto, service.GetAgv(Agv2).Control);
    }

    [Fact]
    public async Task マニュアルとロックには走っているときは切り替えられない()
    {
        var service = await TwoAgvsAsync();
        await service.MoveAgvAsync(Agv2, new PanelId("W"), null, "move", None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ChangeAsync(service, Agv2, ControlStatus.Manual));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ChangeAsync(service, Agv2, ControlStatus.Locked));
    }

    [Fact]
    public async Task マニュアルとロックには待機中か緊急停止中なら切り替えられる()
    {
        var service = await TwoAgvsAsync();
        await service.EmergencyStopAsync(Agv1, None);

        await ChangeAsync(service, Agv1, ControlStatus.Manual);
        await ChangeAsync(service, Agv2, ControlStatus.Locked);

        Assert.Equal(ControlStatus.Manual, service.GetAgv(Agv1).Control);
        Assert.Equal(ControlStatus.Locked, service.GetAgv(Agv2).Control);
    }

    [Fact]
    public async Task 緊急停止中に行き先があってもマニュアルとロックに切り替えられ行き先と残りの経路を捨てる()
    {
        var service = await TwoAgvsAsync();
        await service.EmergencyStopAsync(Agv1, None); // 番号 2
        await service.MoveAgvAsync(Agv1, new PanelId("W"), null, "move", None);

        await ChangeAsync(service, Agv1, ControlStatus.Locked);
        await ChangeAsync(service, Agv1, ControlStatus.Auto);
        await service.ClearAlarmAsync(Agv1, None); // 番号 3
        await service.HandleCommandRequestAsync(Agv1, 2, None);
        await service.HandleAlarmClearedAsync(Agv1, 3, None);

        Assert.Equal(ControlStatus.Auto, service.GetAgv(Agv1).Control);
        Assert.Empty((await service.HandleCommandRequestAsync(Agv1, 3, None)).OfType<MotionCommand>()); // 捨てた行き先へは走り出さない
    }

    [Fact]
    public async Task 異常のときはマニュアルとロックに切り替えられない()
    {
        var service = await TwoAgvsAsync();
        await service.HandleAgvErrorAsync(Agv2, AgvErrorKind.CrossMissed, None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ChangeAsync(service, Agv2, ControlStatus.Locked));
    }

    [Fact]
    public async Task 切り離しにはロック中のときだけ切り替えられ予約を外す()
    {
        var service = await TwoAgvsAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => ChangeAsync(service, Agv2, ControlStatus.Detached));
        await ChangeAsync(service, Agv2, ControlStatus.Locked);
        await ChangeAsync(service, Agv2, ControlStatus.Detached);

        Assert.Equal(ControlStatus.Detached, service.GetAgv(Agv2).Control);
        Assert.False(service.IsReservedByOther(new PanelId("M"), Agv1));
    }

    [Fact]
    public async Task 切り離しから切り替えるには位置を指定し位置リセットが済んだら切り替わる()
    {
        var service = await TwoAgvsAsync();
        await ChangeAsync(service, Agv2, ControlStatus.Locked);
        await ChangeAsync(service, Agv2, ControlStatus.Detached);

        await Assert.ThrowsAsync<ArgumentException>(() => ChangeAsync(service, Agv2, ControlStatus.Auto));
        await ChangeAsync(service, Agv2, ControlStatus.Auto, new Pose("W", 270));
        var beforeCompleted = service.GetAgv(Agv2).Control;
        var commands = await service.HandleCommandRequestAsync(Agv2, 1, None);
        await service.HandlePositionResetCompletedAsync(Agv2, commands.Single().Number, None);

        Assert.Equal(ControlStatus.Detached, beforeCompleted);
        Assert.IsType<PositionResetCommand>(commands.Single());
        Assert.Equal(ControlStatus.Auto, service.GetAgv(Agv2).Control);
        Assert.Equal(new Pose("W", 270), service.GetAgv(Agv2).Position);
    }

    [Fact]
    public async Task 切り離しから戻す位置のパネルをほかのAGVが予約していたら受け付けず切り離しのまま()
    {
        var service = await TwoAgvsAsync();
        await ChangeAsync(service, Agv2, ControlStatus.Locked);
        await ChangeAsync(service, Agv2, ControlStatus.Detached);

        var accepted = await ChangeAsync(service, Agv2, ControlStatus.Auto, new Pose("N", 180)); // N には AGV-1 がいる
        var commands = await service.HandleCommandRequestAsync(Agv2, 1, None);

        Assert.False(accepted);
        Assert.Equal(ControlStatus.Detached, service.GetAgv(Agv2).Control);
        Assert.Empty(commands);
    }

    [Fact]
    public async Task 切り離しから戻す位置のパネルがマップになければ受け付けず切り離しのまま()
    {
        var service = await TwoAgvsAsync();
        await ChangeAsync(service, Agv2, ControlStatus.Locked);
        await ChangeAsync(service, Agv2, ControlStatus.Detached);

        await Assert.ThrowsAsync<ArgumentException>(() => ChangeAsync(service, Agv2, ControlStatus.Auto, new Pose("NOWHERE", 0)));

        Assert.Equal(ControlStatus.Detached, service.GetAgv(Agv2).Control);
        Assert.Empty(await service.HandleCommandRequestAsync(Agv2, 1, None));
    }

    // ---- 受け付けるコマンド ----

    [Fact]
    public async Task ロック中は緊急停止と位置リセットを受け付けアラームクリアとキャリアを上げるは受け付けない()
    {
        var service = await TwoAgvsAsync();
        await ChangeAsync(service, Agv2, ControlStatus.Locked);

        await service.EmergencyStopAsync(Agv2, None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ClearAlarmAsync(Agv2, None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RaiseCarrierAsync(Agv2, "raise", None));
        Assert.True(await service.ResetPositionAsync(Agv2, new Pose("W", 270), None));
    }

    [Fact]
    public async Task 切り離し中はどのコマンドも受け付けない()
    {
        var service = await TwoAgvsAsync();
        await ChangeAsync(service, Agv2, ControlStatus.Locked);
        await ChangeAsync(service, Agv2, ControlStatus.Detached);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EmergencyStopAsync(Agv2, None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResetPositionAsync(Agv2, new Pose("W", 270), None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MoveAgvAsync(Agv2, new PanelId("W"), null, "move", None));
    }

    [Theory]
    [InlineData(ControlStatus.Manual)]
    [InlineData(ControlStatus.Locked)]
    public async Task マニュアルとロックのAGVはシステムの判断では動かせない(ControlStatus status)
    {
        var service = await TwoAgvsAsync();
        await ChangeAsync(service, Agv2, status);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MoveAgvAsync(Agv2, new PanelId("W"), null, "move", None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LowerCarrierAsync(Agv2, "lower", None));
    }

    // ---- 全台の計画での扱い ----

    [Theory]
    [InlineData(ControlStatus.Manual)]
    [InlineData(ControlStatus.Locked)]
    public async Task マニュアルとロックのAGVは今いるパネルを塞ぐ障害物にする(ControlStatus status)
    {
        var service = await TwoAgvsAsync();
        await ChangeAsync(service, Agv2, status);

        var result = await service.MoveAgvAsync(Agv1, new PanelId("S"), null, "move", None);

        Assert.Equal(MoveResult.Waiting, result);
    }

    [Fact]
    public async Task 切り離したAGVは計画に入れずそのパネルは通れるようになる()
    {
        var service = await TwoAgvsAsync();
        await ChangeAsync(service, Agv2, ControlStatus.Locked);
        var whileLocked = await service.MoveAgvAsync(Agv1, new PanelId("S"), null, "move", None);

        await ChangeAsync(service, Agv2, ControlStatus.Detached);
        var motion = (await service.HandleCommandRequestAsync(Agv1, 1, None)).OfType<MotionCommand>().SingleOrDefault();

        Assert.Equal(MoveResult.Waiting, whileLocked);
        Assert.NotNull(motion); // 切り離したら計画を立て直し、塞がれていた AGV-1 が出発する
    }
}
