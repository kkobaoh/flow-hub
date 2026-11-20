using FlowHub.BuildingBlocks;
using FlowHub.Consumables;
using FlowHub.Consumables.Application;
using FlowHub.Dispatching;
using FlowHub.Map;
using FlowHub.ToolPlanning.Application;

namespace FlowHub.ToolPlanning.Tests;

public class ToolPlanningSubscriptionsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task 在庫が変わったら判断をやり直し搬送の完了を受けたら要求を忘れる()
    {
        // 消耗品の管理が公開する StockChanged を、同じバスで搬送計画が受ける
        var time = new ManualTimeProvider(T0);
        var dispatching = new FakeDispatching();
        var subscriptions = new EventSubscriptions();
        var bus = new InProcessEventBus(subscriptions);
        using var consumables = new ConsumablesService(dispatching, new NoDevices(), new NoStations(), new RecordingReplenishment(), bus, time);
        using var planning = new ToolPlanningService(consumables, dispatching, time);
        ToolPlanningSubscriptions.Register(subscriptions, planning);

        var device = new DeviceId("D1");
        var variety = new VarietyId("A");
        consumables.RegisterDevice(new DeviceLayout(device, new PanelId("D"), SlotsPerStocker: [3]));
        consumables.RegisterStation(new StationLayout(new StationId("S1"), new PanelId("S")));
        consumables.RegisterProcessType(new ProcessTypeSettings(new ProcessTypeId("P"), new CarrierTypeId("X"), CarrierCapacity: 2));
        consumables.RegisterVariety(variety, new ProcessTypeId("P"));
        consumables.RegisterExpectedArrival(new ConsumableId("A-1"), variety);
        await consumables.ReportReadAtStationAsync(new StationId("S1"), new ConsumableId("A-1"), None);

        await consumables.SetStockerSettingAsync(device, new Dictionary<VarietyId, int> { [variety] = 1 }, None);
        await bus.FlushAsync(None);

        Assert.Equal([["A-1"]], dispatching.ActiveOrders.Select(o => o.CargoRefs));

        // 2回のやり直しで出し直しているので、残っている要求の ID で完了を知らせる
        dispatching.AssignAll();
        var active = dispatching.Orders.Single(o => !dispatching.Cancelled.Contains(o.Id)).Id;
        await bus.PublishAsync(new TransportCompleted(EventMetadata.Start("test", time), active, ConsumablesService.Requester), None);
        await bus.FlushAsync(None);
        var callsBefore = dispatching.CancelCalls;
        await planning.ReplanAsync(None);

        Assert.Equal([typeof(StockChanged), typeof(TransportCompleted), typeof(OperationStarted)], subscriptions.SubscribedEventTypes);
        Assert.Equal(callsBefore, dispatching.CancelCalls); // 完了した搬送は、もう取りやめようとしない
    }

    [Fact]
    public async Task 運転を始めたら判断をやり直す()
    {
        var time = new ManualTimeProvider(T0);
        var dispatching = new FakeDispatching();
        var subscriptions = new EventSubscriptions();
        var bus = new InProcessEventBus(subscriptions);
        var replenishment = new RecordingReplenishment();
        using var consumables = new ConsumablesService(dispatching, new NoDevices(), new NoStations(), replenishment, new IgnoringPublisher(), time);
        using var planning = new ToolPlanningService(consumables, dispatching, time);
        ToolPlanningSubscriptions.Register(subscriptions, planning);
        var device = new DeviceId("D1");
        var variety = new VarietyId("A");
        consumables.RegisterDevice(new DeviceLayout(device, new PanelId("D"), SlotsPerStocker: [3]));
        consumables.RegisterProcessType(new ProcessTypeSettings(new ProcessTypeId("P"), new CarrierTypeId("X"), CarrierCapacity: 2));
        consumables.RegisterVariety(variety, new ProcessTypeId("P"));
        await consumables.SetStockerSettingAsync(device, new Dictionary<VarietyId, int> { [variety] = 1 }, None);

        await bus.PublishAsync(new OperationStarted(EventMetadata.Start("operation", time)), None);
        await bus.FlushAsync(None);

        Assert.Equal([(variety, 1)], replenishment.Requests); // ステーションにないので補充を頼んだ
    }
}
