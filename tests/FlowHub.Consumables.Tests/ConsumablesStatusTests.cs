using FlowHub.BuildingBlocks;
using FlowHub.Consumables.Application;
using FlowHub.Consumables.Domain;
using FlowHub.Dispatching;
using FlowHub.Map;

namespace FlowHub.Consumables.Tests;

/// <summary>
/// 画面に出す装置とステーションの様子（気をつけること、ストッカー、在庫）のテスト。
/// </summary>
public class ConsumablesStatusTests
{
    private static readonly DeviceId Device = new("D1");
    private static readonly StationId Station = new("S1");
    private static readonly ProcessTypeId Process = new("P");
    private static readonly VarietyId VarietyA = new("A");
    private static readonly VarietyId VarietyB = new("B");
    private static readonly VarietyId VarietyZ = new("Z");
    private static readonly CancellationToken None = CancellationToken.None;

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);
    }

    private sealed class NoDispatching : IDispatchingService
    {
        public bool IsRunning => true;

        public Task<TransportCancelResult> CancelAsync(TransportId id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<TransportId> RequestTransportFromAgvAsync(AgvTransportOrder order, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<TransportId> RequestTransportAsync(TransportOrder order, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> CancelTransportAsync(TransportId id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ReportTransferCompletedAsync(TransportId id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoDevices : IDeviceGateway
    {
        public Task GrantTransferAsync(DeviceId device, TransportId transport, TransferSide side, IReadOnlyList<CargoSlot> cargo, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoStations : IStationGateway
    {
        public Task GrantTransferAsync(StationId station, TransportId transport, TransferSide side, IReadOnlyList<ConsumableId> cargo, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RequestShipmentAsync(StationId station, ConsumableId consumable, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>装置 D1（ストッカー1つ、スロット3つ）と、ステーション S1。品種 A、B、Z は加工の種類 P。</summary>
    private static ConsumablesService Setup()
    {
        var service = new ConsumablesService(new NoDispatching(), new NoDevices(), new NoStations(), new IgnoringReplenishment(), new IgnoringPublisher(), new FixedTimeProvider());
        service.RegisterDevice(new DeviceLayout(Device, new PanelId("D"), SlotsPerStocker: [3]));
        service.RegisterStation(new StationLayout(Station, new PanelId("S")));
        service.RegisterProcessType(new ProcessTypeSettings(Process, new CarrierTypeId("X"), CarrierCapacity: 2));
        service.RegisterVariety(VarietyA, Process);
        service.RegisterVariety(VarietyB, Process);
        service.RegisterVariety(VarietyZ, Process);
        return service;
    }

    private static ConsumableId InStocker(ConsumablesService service, string id, VarietyId variety, int slot, ConsumableCondition condition = ConsumableCondition.New)
    {
        var consumable = new ConsumableId(id);
        service.RegisterConsumable(consumable, variety, Process, condition, new InStocker(Device, new SlotRef(1, slot)));
        return consumable;
    }

    private static ConsumableId AtStation(ConsumablesService service, string id, VarietyId variety)
    {
        var consumable = new ConsumableId(id);
        service.RegisterConsumable(consumable, variety, Process, ConsumableCondition.New, new AtStation(Station));
        return consumable;
    }

    private static TransferPermissionRequested Permission(string endpoint, TransferSide side, params ConsumableId[] cargo) =>
        new(EventMetadata.Start("test", new FixedTimeProvider()), new TransportId(1), ConsumablesService.Requester, cargo.Select(c => c.Value).ToArray(), endpoint, side);

    private static DeviceStatus DeviceOf(ConsumablesService service) => Assert.Single(service.ReadStatuses().Devices);

    private static StationStatus StationOf(ConsumablesService service) => Assert.Single(service.ReadStatuses().Stations);

    [Fact]
    public async Task 不足は届ける途中のものを引いたまだ手当てのない数()
    {
        var service = Setup();
        await service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 3 }, None);
        InStocker(service, "A1", VarietyA, 1);
        var a2 = AtStation(service, "A2", VarietyA);
        service.ReserveForDelivery(Device, [a2]);

        var status = DeviceOf(service);

        Assert.Equal(1, status.Shortage);
        Assert.Equal([new StockerLine(VarietyA, Target: 3, Have: 2)], status.Stocker);
        Assert.Equal(new SlotSummary(Total: 3, Free: 1, Delivering: 1), status.Slots);
    }

    [Fact]
    public async Task 回収待ちは回収が要ると判断してから装置から取り出すまで頼んだものも頼む前のものも数える()
    {
        var service = Setup();
        await service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 1 }, None);
        InStocker(service, "A1", VarietyA, 1);
        InStocker(service, "Z1", VarietyZ, 2); // 設定にない品種
        var z2 = InStocker(service, "Z2", VarietyZ, 3);
        service.ReserveForRecovery([z2]); // 回収を頼んだ

        var status = DeviceOf(service);

        Assert.Equal(2, status.CollectWaiting);
        Assert.Contains(new StockerLine(VarietyZ, Target: 0, Have: 2), status.Stocker); // 設定にない品種も、ある数を出す
    }

    [Fact]
    public async Task ID食い違いは照合のエラーが来てからそのスロットが片付くまで()
    {
        var service = Setup();
        InStocker(service, "A1", VarietyA, 2);

        await service.ReportIdMismatchAsync(Device, new SlotRef(1, 2), new ConsumableId("X9"), None);
        Assert.Equal([new SlotRef(1, 2)], DeviceOf(service).MismatchedSlots);

        // 人が交換しても、現物を確かめるまでスロットはふさがったまま
        await service.ReportReplacedAsync(Device, new SlotRef(1, 2), new ConsumableId("A1"), None);
        Assert.Equal([new SlotRef(1, 2)], DeviceOf(service).MismatchedSlots);

        // AGV で取り出したら、片付いた
        var a1 = new ConsumableId("A1");
        await service.OnTransferPermissionRequestedAsync(Permission(Endpoints.Device(Device), TransferSide.Load, a1), None);
        await service.ReportTransferCompletedAsync(new TransportId(1), [a1], None);
        Assert.Empty(DeviceOf(service).MismatchedSlots);
    }

    [Fact]
    public async Task 段取り替えのエラーは設定を変えたときか次のリクエストが通ったときに消える()
    {
        var service = Setup();
        await service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 1 }, None);

        await service.ReportSetupRequestedAsync(Device, VarietyB, None);
        await service.ReportSetupRequestedAsync(Device, VarietyZ, None);
        Assert.Equal([VarietyB, VarietyZ], DeviceOf(service).SetupErrors);

        await service.ReportSetupRequestedAsync(Device, VarietyA, None); // 通った
        Assert.Empty(DeviceOf(service).SetupErrors);

        await service.ReportSetupRequestedAsync(Device, VarietyB, None);
        await service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 1, [VarietyB] = 1 }, None);
        Assert.Empty(DeviceOf(service).SetupErrors);
    }

    [Fact]
    public async Task ステーションは要確認の数と品種ごとの届けられる数と補充を頼んでまだ入っていない数を出す()
    {
        var service = Setup();
        AtStation(service, "A1", VarietyA);
        var a2 = AtStation(service, "A2", VarietyA);
        service.ReserveForDelivery(Device, [a2]); // 届ける予約があるものは、届けられる数に入れない
        await service.ReportReadAtStationAsync(Station, new ConsumableId("UNKNOWN-1"), None); // 登録されていない ID は要確認
        await service.RequestReplenishmentAsync(VarietyB, 2, None);

        var status = StationOf(service);

        Assert.Equal(1, status.NeedsCheck);
        Assert.Equal(2, status.ReplenishWaiting);
        Assert.Equal(
            [new StationLine(VarietyA, Available: 1, ReplenishWaiting: 0), new StationLine(VarietyB, Available: 0, ReplenishWaiting: 2), new StationLine(VarietyZ, Available: 0, ReplenishWaiting: 0)],
            status.Stock);
    }
}
