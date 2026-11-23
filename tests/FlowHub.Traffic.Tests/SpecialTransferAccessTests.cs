using FlowHub.BuildingBlocks;
using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic.Application;

namespace FlowHub.Traffic.Tests;

/// <summary>
/// 特殊消耗品受け渡しパネルを走れる AGV と走れない AGV のテスト。
/// </summary>
/// <remarks>
/// A →(90度) X →(90度) B。X は特殊消耗品受け渡しパネル（キャリアを下ろす角度は 45度）。AGV は A で東（90度）を向いている。
/// </remarks>
public class SpecialTransferAccessTests
{
    private static readonly AgvId Agv = new("AGV-1");
    private static readonly MotionProfile Motion = new(500, 250, 90);
    private static readonly CancellationToken None = CancellationToken.None;

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
    }

    private sealed class IgnoringPublisher : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
            where TEvent : IIntegrationEvent => Task.CompletedTask;
    }

    private static PanelId P(string id) => new(id);

    private static async Task<TrafficService> ReadyAsync(bool canTravelSpecialTransfer, ControlStatus control = ControlStatus.Auto)
    {
        var store = new InMemoryMapStore();
        store.Publish(MapSnapshot.Create(
            [
                new Panel(P("A"), PanelType.Normal, false),
                new Panel(P("X"), PanelType.SpecialTransfer, false) { CarrierLoweringHeading = new Heading(45) },
                new Panel(P("B"), PanelType.Normal, false),
            ],
            [
                new Connection(P("A"), P("X"), 800, new Heading(90), OneWay: false),
                new Connection(P("X"), P("B"), 800, new Heading(90), OneWay: false),
            ]).Snapshot!);
        var service = new TrafficService(store, new IgnoringPublisher(), new FixedTimeProvider());
        service.RegisterAgv(Agv, Motion, canTravelSpecialTransfer);
        await service.ResetPositionAsync(Agv, new Pose("A", 90), None);
        await service.HandleCommandRequestAsync(Agv, -1, None);
        await service.HandlePositionResetCompletedAsync(Agv, 1, None);
        await service.ChangeControlStatusAsync(Agv, control, position: null, None);
        return service;
    }

    [Fact]
    public async Task 走れないAGVは特殊消耗品受け渡しパネルを通れない()
    {
        var service = await ReadyAsync(canTravelSpecialTransfer: false);

        Assert.Null(service.EstimateMoveCost(Agv, P("B")));
        Assert.Equal(MoveResult.NoRoute, await service.MoveAgvAsync(Agv, P("B"), null, "move", None));
        Assert.Equal(MoveResult.NoRoute, await service.MoveAgvAsync(Agv, P("X"), null, "move", None));
    }

    [Fact]
    public async Task 走れるAGVは特殊消耗品受け渡しパネルを通れる()
    {
        var service = await ReadyAsync(canTravelSpecialTransfer: true);

        Assert.NotNull(service.EstimateMoveCost(Agv, P("B")));
        Assert.Equal(MoveResult.Started, await service.MoveAgvAsync(Agv, P("B"), null, "move", None));
    }

    [Fact]
    public async Task 走れないAGVはマニュアルの1つずつの動作でも特殊消耗品受け渡しパネルに入れない()
    {
        var service = await ReadyAsync(canTravelSpecialTransfer: false, ControlStatus.Manual);

        Assert.Equal(ManualStepResult.NotAllowed, await service.ManualStepAsync(Agv, new ManualForward(), None));
        Assert.Equal(ManualMoveResult.NoRoute, await service.ManualMoveAsync(Agv, P("B"), arrival: null, None));
    }

    [Fact]
    public async Task 走れるAGVはマニュアルの1つずつの動作で特殊消耗品受け渡しパネルに入れる()
    {
        var service = await ReadyAsync(canTravelSpecialTransfer: true, ControlStatus.Manual);

        Assert.Equal(ManualStepResult.Accepted, await service.ManualStepAsync(Agv, new ManualForward(), None));
    }

    [Fact]
    public async Task AGVの様子に特殊消耗品受け渡しパネルを走れるかが載る()
    {
        var canTravel = await ReadyAsync(canTravelSpecialTransfer: true);
        var cannot = await ReadyAsync(canTravelSpecialTransfer: false);

        Assert.True(canTravel.GetAgv(Agv).CanTravelSpecialTransfer);
        Assert.False(cannot.GetAgv(Agv).CanTravelSpecialTransfer);
    }
}
