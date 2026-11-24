using FlowHub.BuildingBlocks;
using FlowHub.Consumables.Application;
using FlowHub.Consumables.Domain;
using FlowHub.Dispatching;
using FlowHub.Map;

namespace FlowHub.Consumables.Tests;

/// <summary>
/// 届ける消耗品の指名のテスト。
/// </summary>
public class ConsumablesDesignationTests
{
    private static readonly DeviceId Device = new("D1");
    private static readonly DeviceId OtherDevice = new("D2");
    private static readonly StationId Station = new("S1");
    private static readonly ProcessTypeId Process = new("P");
    private static readonly VarietyId VarietyA = new("A");
    private static readonly ConsumableId A1 = new("A-1");
    private static readonly ConsumableId A2 = new("A-2");
    private static readonly CancellationToken None = CancellationToken.None;

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
    }

    private sealed class NoDispatching : IDispatchingService
    {
        public bool IsRunning => true;

        public Task<TransportCancelResult> CancelAsync(TransportId id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TransportId> RequestTransportFromAgvAsync(AgvTransportOrder order, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TransportId> RequestTransportAsync(TransportOrder order, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> CancelTransportAsync(TransportId id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ReportTransferCompletedAsync(TransportId id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoDevices : IDeviceGateway
    {
        public Task GrantTransferAsync(DeviceId device, TransportId transport, TransferSide side, IReadOnlyList<CargoSlot> cargo, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class NoStations : IStationGateway
    {
        public Task GrantTransferAsync(StationId station, TransportId transport, TransferSide side, IReadOnlyList<ConsumableId> cargo, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RequestShipmentAsync(StationId station, ConsumableId consumable, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// 装置 D1 と D2、ステーション S1。A-1、A-2 はステーションにある品種 A の新品。
    /// </summary>
    private static ConsumablesService Setup()
    {
        var service = new ConsumablesService(new NoDispatching(), new NoDevices(), new NoStations(), new IgnoringReplenishment(), new IgnoringPublisher(), new FixedTimeProvider());
        service.RegisterDevice(new DeviceLayout(Device, new PanelId("D"), SlotsPerStocker: [3]));
        service.RegisterDevice(new DeviceLayout(OtherDevice, new PanelId("D2"), SlotsPerStocker: [3]));
        service.RegisterStation(new StationLayout(Station, new PanelId("S")));
        service.RegisterProcessType(new ProcessTypeSettings(Process, new CarrierTypeId("X"), CarrierCapacity: 2));
        service.RegisterVariety(VarietyA, Process);
        service.RegisterConsumable(A1, VarietyA, Process, ConsumableCondition.New, new AtStation(Station));
        service.RegisterConsumable(A2, VarietyA, Process, ConsumableCondition.New, new AtStation(Station));
        return service;
    }

    private static TransferPermissionRequested Permission(TransportId transport, string endpoint, TransferSide side, params ConsumableId[] cargo) =>
        new(EventMetadata.Start("test", new FixedTimeProvider()), transport, ConsumablesService.Requester, cargo.Select(c => c.Value).ToArray(), endpoint, side);

    [Fact]
    public void 指名した消耗品はその装置の指名として返りほかの装置の候補から外れる()
    {
        using var service = Setup();

        service.Designate(Device, A2);

        Assert.Equal([A2], service.GetDesignated(Device, VarietyA));
        Assert.Empty(service.GetDesignated(OtherDevice, VarietyA));
        Assert.Equal([A1], service.GetStationStock(VarietyA).Select(s => s.Consumable));
    }

    [Fact]
    public void ステーションにある要確認でない新品でなければ指名できない()
    {
        using var service = Setup();
        var used = new ConsumableId("U-1");
        service.RegisterConsumable(used, VarietyA, Process, ConsumableCondition.Used, new AtStation(Station));
        var inStocker = new ConsumableId("S-1");
        service.RegisterConsumable(inStocker, VarietyA, Process, ConsumableCondition.New, new InStocker(Device, new SlotRef(1, 1)));

        Assert.Throws<InvalidOperationException>(() => service.Designate(Device, used));
        Assert.Throws<InvalidOperationException>(() => service.Designate(Device, inStocker));
    }

    [Fact]
    public async Task 要確認の消耗品と出庫を頼んだ消耗品と届ける予約がある消耗品は指名できない()
    {
        using var service = Setup();
        var x = new ConsumableId("X-1");
        await service.ReportReadAtStationAsync(Station, x, None);
        await service.RequestShipmentAsync(A1, None);
        service.ReserveForDelivery(OtherDevice, [A2]);

        Assert.Throws<InvalidOperationException>(() => service.Designate(Device, x));
        Assert.Throws<InvalidOperationException>(() => service.Designate(Device, A1));
        Assert.Throws<InvalidOperationException>(() => service.Designate(Device, A2));
    }

    [Fact]
    public void 指名済みの消耗品はもう一度指名できない()
    {
        using var service = Setup();
        service.Designate(Device, A2);

        Assert.Throws<InvalidOperationException>(() => service.Designate(OtherDevice, A2));
    }

    [Fact]
    public void 指名した消耗品に届ける予約をしても指名は残る()
    {
        // 割り当て前の要求を取りやめて出し直すときに、また指名から選べるようにするため
        using var service = Setup();
        service.Designate(Device, A2);

        service.ReserveForDelivery(Device, [A2]);
        service.ReleaseReservations([A2]);

        Assert.Equal([A2], service.GetDesignated(Device, VarietyA));
    }

    [Fact]
    public void 予約している間は指名の候補に出さない()
    {
        using var service = Setup();
        service.Designate(Device, A2);

        service.ReserveForDelivery(Device, [A2]);

        Assert.Empty(service.GetDesignated(Device, VarietyA));
    }

    [Fact]
    public async Task 積み込んだり出庫を頼んだりして指名の条件に合わなくなったら指名は外れる()
    {
        using var service = Setup();
        service.Designate(Device, A1);
        service.Designate(Device, A2);
        await service.OnTransferPermissionRequestedAsync(Permission(new TransportId(1), Endpoints.Station(Station), TransferSide.Load, A1), None);
        await service.ReportTransferCompletedAsync(new TransportId(1), [A1], None);

        await service.RequestShipmentAsync(A2, None);

        Assert.Empty(service.GetDesignated(Device, VarietyA));
        Assert.False(service.IsDesignated(A1));
        Assert.False(service.IsDesignated(A2));
    }

    [Fact]
    public void 指名を取り消すと先入れ先出しの候補に戻る()
    {
        using var service = Setup();
        service.Designate(Device, A2);

        service.CancelDesignation(A2);

        Assert.Empty(service.GetDesignated(Device, VarietyA));
        Assert.Equal([A1, A2], service.GetStationStock(VarietyA).Select(s => s.Consumable));
    }
}
