using FlowHub.BuildingBlocks;
using FlowHub.Dispatching.Application;
using FlowHub.Map;
using FlowHub.Traffic;

namespace FlowHub.Dispatching.Tests;

/// <summary>
/// 搬送元か搬送先が特殊消耗品受け渡しパネルの搬送を、走れる AGV にだけ割り当てることのテスト。
/// </summary>
/// <remarks>
/// 通常パネル A に、受け渡しパネル S（ステーション）と、特殊消耗品受け渡しパネル X（装置）がつながる。
/// AGV-1 は走れない種類で搬送元に近く、AGV-2 は走れる種類で遠い。
/// </remarks>
public class DispatchingSpecialTransferTests
{
    private static readonly AgvId Agv1 = new("AGV-1");
    private static readonly AgvId Agv2 = new("AGV-2");
    private static readonly CarrierTypeId CarrierX = new("X");
    private static readonly PanelId A = new("A");
    private static readonly PanelId S = new("S");
    private static readonly PanelId X = new("X");
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class IgnoringPublisher : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
            where TEvent : IIntegrationEvent => Task.CompletedTask;
    }

    private static InMemoryMapStore Map()
    {
        var store = new InMemoryMapStore();
        store.Publish(MapSnapshot.Create(
            [
                new Panel(A, PanelType.Normal, false),
                new Panel(S, PanelType.Transfer, false),
                new Panel(X, PanelType.SpecialTransfer, false) { CarrierLoweringHeading = new Heading(45) },
            ],
            [new Connection(A, S, 800, new Heading(0), false), new Connection(A, X, 800, new Heading(90), false)]).Snapshot!);
        return store;
    }

    private static (DispatchingService Service, FakeFleetTraffic Traffic) Setup(bool includeCapable = true)
    {
        var traffic = new FakeFleetTraffic();
        var service = new DispatchingService(traffic, Map(), new IgnoringPublisher(), new FixedTimeProvider());
        traffic.Set(Agv1, A);
        traffic.Costs[(Agv1, S)] = TimeSpan.FromSeconds(1);
        traffic.Costs[(Agv1, X)] = TimeSpan.FromSeconds(1);
        service.RegisterAgv(Agv1, CarrierX, goChargeBelowPercent: 20, resumeAtPercent: 80);
        if (includeCapable)
        {
            traffic.Set(Agv2, A, canTravelSpecialTransfer: true);
            traffic.Costs[(Agv2, S)] = TimeSpan.FromSeconds(9);
            traffic.Costs[(Agv2, X)] = TimeSpan.FromSeconds(9);
            service.RegisterAgv(Agv2, CarrierX, goChargeBelowPercent: 20, resumeAtPercent: 80);
        }

        return (service, traffic);
    }

    private static TransportOrder Order(PanelId source, PanelId destination) =>
        new("test", ["C-1"], source, "source", destination, "destination", CarrierX, Now.AddMinutes(10));

    [Fact]
    public async Task 搬送先が特殊消耗品受け渡しパネルなら走れるAGVにだけ割り当てる()
    {
        var (service, traffic) = Setup();

        var id = await service.RequestTransportAsync(Order(S, X), None);

        Assert.Equal((Agv2, S, $"transport:{id}"), Assert.Single(traffic.Moves));
    }

    [Fact]
    public async Task 搬送元が特殊消耗品受け渡しパネルなら走れるAGVにだけ割り当てる()
    {
        var (service, traffic) = Setup();

        var id = await service.RequestTransportAsync(Order(X, S), None);

        Assert.Equal((Agv2, X, $"transport:{id}"), Assert.Single(traffic.Moves));
    }

    [Fact]
    public async Task 走れるAGVがいなければ割り当てずに待つ()
    {
        var (service, traffic) = Setup(includeCapable: false);

        await service.RequestTransportAsync(Order(S, X), None);

        Assert.Empty(traffic.Moves);
    }

    [Fact]
    public async Task 特殊消耗品受け渡しパネルを使わない搬送は走れないAGVにも割り当てる()
    {
        var (service, traffic) = Setup();

        var id = await service.RequestTransportAsync(Order(S, S), None);

        Assert.Equal((Agv1, S, $"transport:{id}"), Assert.Single(traffic.Moves));
    }
}
