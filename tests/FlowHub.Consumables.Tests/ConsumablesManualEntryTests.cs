using FlowHub.BuildingBlocks;
using FlowHub.Consumables.Application;
using FlowHub.Dispatching;
using FlowHub.Map;

namespace FlowHub.Consumables.Tests;

/// <summary>
/// 在庫の手入力（消耗品の検索の画面からの、登録・直す・消す）のテスト。
/// </summary>
public class ConsumablesManualEntryTests
{
    private static readonly DeviceId Device = new("D1");
    private static readonly StationId Station = new("S1");
    private static readonly ProcessTypeId Process = new("P");
    private static readonly VarietyId VarietyA = new("A");
    private static readonly VarietyId VarietyB = new("B");
    private static readonly DateTimeOffset T0 = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = T0;

        public override DateTimeOffset GetUtcNow() => Now;
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

    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<IIntegrationEvent> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
            where TEvent : IIntegrationEvent
        {
            Published.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed record Fixture(ConsumablesService Service, ManualTime Time, RecordingPublisher Events);

    /// <summary>装置 D1（ストッカー1つ、スロット2つ）と、ステーション S1。品種 A、B は加工の種類 P。</summary>
    private static Fixture Setup()
    {
        var time = new ManualTime();
        var events = new RecordingPublisher();
        var service = new ConsumablesService(new NoDispatching(), new NoDevices(), new NoStations(), new IgnoringReplenishment(), events, time);
        service.RegisterDevice(new DeviceLayout(Device, new PanelId("D"), SlotsPerStocker: [2]));
        service.RegisterStation(new StationLayout(Station, new PanelId("S")));
        service.RegisterProcessType(new ProcessTypeSettings(Process, new CarrierTypeId("X"), CarrierCapacity: 2));
        service.RegisterVariety(VarietyA, Process);
        service.RegisterVariety(VarietyB, Process);
        return new Fixture(service, time, events);
    }

    private static ConsumableRecord RecordOf(Fixture f, string id) => f.Service.ReadConsumables().Single(c => c.Id.Value == id);

    private static StationItem ItemOf(Fixture f, string id) => f.Service.ReadStatuses().Stations.Single().Items.Single(i => i.Id.Value == id);

    private static readonly ConsumableLocation AtS1 = new AtStation(Station);

    private static InStocker Slot(int slot) => new(Device, new SlotRef(1, slot));

    // ---- 登録 ----

    [Fact]
    public async Task ステーションに登録すると入庫した日時を登録した日時にし入庫の予定から外す()
    {
        var f = Setup();
        f.Service.RegisterExpectedArrival(new ConsumableId("T-1"), VarietyB);
        await f.Service.RequestReplenishmentAsync(VarietyA, 1, None);
        f.Time.Now = T0.AddMinutes(5);

        var result = await f.Service.RegisterManuallyAsync(new ConsumableId("T-1"), VarietyA, ConsumableCondition.New, AtS1, None);

        Assert.Equal(ManualEntryResult.Saved, result);
        Assert.Equal(new ConsumableRecord(new ConsumableId("T-1"), VarietyA, Process, ConsumableCondition.New, AtS1, NeedsCheckReason.NotNeeded, new NoReservation(), null, false), RecordOf(f, "T-1"));
        Assert.Equal(T0.AddMinutes(5), ItemOf(f, "T-1").ArrivedAt);
        Assert.Equal(1, f.Service.ReadStatuses().Stations.Single().ReplenishWaiting); // 補充待ちは減らさない
        await f.Service.ReportReadAtStationAsync(Station, new ConsumableId("T-2"), None);
        Assert.Equal(NeedsCheckReason.UnregisteredId, RecordOf(f, "T-2").Check); // 予定から外したので、ほかの ID はそのまま
        Assert.Single(f.Events.Published.OfType<StockChanged>(), e => e.Metadata.CorrelationId.Contains("T-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task 空いているスロットに登録でき登録済みのIDや埋まっているスロットは断る()
    {
        var f = Setup();
        Assert.Equal(ManualEntryResult.Saved, await f.Service.RegisterManuallyAsync(new ConsumableId("T-1"), VarietyA, ConsumableCondition.Used, Slot(1), None));

        Assert.Equal(ManualEntryResult.AlreadyRegistered, await f.Service.RegisterManuallyAsync(new ConsumableId("T-1"), VarietyA, ConsumableCondition.New, Slot(2), None));
        Assert.Equal(ManualEntryResult.SlotTaken, await f.Service.RegisterManuallyAsync(new ConsumableId("T-2"), VarietyA, ConsumableCondition.New, Slot(1), None));
        Assert.Equal(Slot(1), RecordOf(f, "T-1").Location);
        Assert.Equal(ConsumableCondition.Used, RecordOf(f, "T-1").Condition);
    }

    [Fact]
    public async Task 所在はステーションかストッカーのスロットに限り知らない品種や装置にないスロットは頼みの誤り()
    {
        var f = Setup();

        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.RegisterManuallyAsync(new ConsumableId("T-1"), VarietyA, ConsumableCondition.New, new UnknownLocation(), None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => f.Service.RegisterManuallyAsync(new ConsumableId("T-1"), VarietyA, ConsumableCondition.New, Slot(9), None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.RegisterManuallyAsync(new ConsumableId("T-1"), new VarietyId("Q"), ConsumableCondition.New, AtS1, None));
        Assert.Empty(f.Service.ReadConsumables());
    }

    // ---- 直す ----

    [Fact]
    public async Task 直すと品種と状態と所在を変え要確認を外し予約と指名を解く()
    {
        var f = Setup();
        await f.Service.ReportReadAtStationAsync(Station, new ConsumableId("X-1"), None); // 要確認
        var t1 = new ConsumableId("T-1");
        f.Service.RegisterConsumable(t1, VarietyA, Process, ConsumableCondition.New, AtS1);
        f.Service.Designate(Device, t1);
        var t2 = new ConsumableId("T-2");
        f.Service.RegisterConsumable(t2, VarietyA, Process, ConsumableCondition.New, AtS1);
        f.Service.ReserveForDelivery(Device, [t2]);

        Assert.Equal(ManualEntryResult.Saved, await f.Service.EditManuallyAsync(new ConsumableId("X-1"), VarietyB, ConsumableCondition.Used, AtS1, None));
        Assert.Equal(ManualEntryResult.Saved, await f.Service.EditManuallyAsync(t1, VarietyB, ConsumableCondition.New, AtS1, None));
        Assert.Equal(ManualEntryResult.Saved, await f.Service.EditManuallyAsync(t2, VarietyA, ConsumableCondition.New, Slot(1), None));

        Assert.Equal(new ConsumableRecord(new ConsumableId("X-1"), VarietyB, Process, ConsumableCondition.Used, AtS1, NeedsCheckReason.NotNeeded, new NoReservation(), null, false), RecordOf(f, "X-1"));
        Assert.Null(RecordOf(f, "T-1").DesignatedFor);
        Assert.Equal(new NoReservation(), RecordOf(f, "T-2").Reservation);
        Assert.Equal(Slot(1), RecordOf(f, "T-2").Location);
    }

    [Fact]
    public async Task ステーションのまま直したら入庫した日時を変えずほかからステーションへ直したら直した日時にする()
    {
        var f = Setup();
        f.Service.RegisterConsumable(new ConsumableId("T-1"), VarietyA, Process, ConsumableCondition.New, AtS1);
        f.Service.RegisterConsumable(new ConsumableId("T-2"), VarietyA, Process, ConsumableCondition.New, Slot(1));
        f.Time.Now = T0.AddMinutes(10);

        await f.Service.EditManuallyAsync(new ConsumableId("T-1"), VarietyB, ConsumableCondition.New, AtS1, None);
        await f.Service.EditManuallyAsync(new ConsumableId("T-2"), VarietyA, ConsumableCondition.New, AtS1, None);

        Assert.Equal(T0, ItemOf(f, "T-1").ArrivedAt);
        Assert.Equal(T0.AddMinutes(10), ItemOf(f, "T-2").ArrivedAt);
    }

    [Fact]
    public async Task 今のスロットのまま直せてほかのものがあるスロットへは直せない()
    {
        var f = Setup();
        f.Service.RegisterConsumable(new ConsumableId("T-1"), VarietyA, Process, ConsumableCondition.New, Slot(1));
        f.Service.RegisterConsumable(new ConsumableId("T-2"), VarietyA, Process, ConsumableCondition.New, Slot(2));

        Assert.Equal(ManualEntryResult.Saved, await f.Service.EditManuallyAsync(new ConsumableId("T-1"), VarietyB, ConsumableCondition.New, Slot(1), None));
        Assert.Equal(ManualEntryResult.SlotTaken, await f.Service.EditManuallyAsync(new ConsumableId("T-1"), VarietyB, ConsumableCondition.New, Slot(2), None));
        Assert.Equal(Slot(1), RecordOf(f, "T-1").Location);
    }

    [Fact]
    public async Task AGVの中と加工部と受け渡し部にあるものと出庫を頼んだものは直すも消すも断り記録にないIDも断る()
    {
        var f = Setup();
        f.Service.RegisterConsumable(new ConsumableId("A-1"), VarietyA, Process, ConsumableCondition.New, new OnAgv(new TransportId(1)));
        f.Service.RegisterConsumable(new ConsumableId("A-2"), VarietyA, Process, ConsumableCondition.Used, new InProcessing(Device, null));
        f.Service.RegisterConsumable(new ConsumableId("A-3"), VarietyA, Process, ConsumableCondition.New, new AtTransferUnit(Device, null));
        f.Service.RegisterConsumable(new ConsumableId("A-4"), VarietyA, Process, ConsumableCondition.New, AtS1);
        await f.Service.RequestShipmentAsync(new ConsumableId("A-4"), None);

        foreach (var id in new[] { "A-1", "A-2", "A-3", "A-4" })
        {
            Assert.Equal(ManualEntryResult.NotEditable, await f.Service.EditManuallyAsync(new ConsumableId(id), VarietyB, ConsumableCondition.New, AtS1, None));
            Assert.Equal(ManualEntryResult.NotEditable, await f.Service.DeleteManuallyAsync(new ConsumableId(id), None));
        }

        Assert.Equal(ManualEntryResult.NotFound, await f.Service.EditManuallyAsync(new ConsumableId("Z-9"), VarietyB, ConsumableCondition.New, AtS1, None));
        Assert.Equal(ManualEntryResult.NotFound, await f.Service.DeleteManuallyAsync(new ConsumableId("Z-9"), None));
        Assert.Equal(4, f.Service.ReadConsumables().Count);
    }

    // ---- 消す ----

    [Fact]
    public async Task 消すと記録から消し予約と指名を解く()
    {
        var f = Setup();
        var t1 = new ConsumableId("T-1");
        f.Service.RegisterConsumable(t1, VarietyA, Process, ConsumableCondition.New, AtS1);
        f.Service.Designate(Device, t1);
        f.Service.ReserveForDelivery(Device, [t1]);

        Assert.Equal(ManualEntryResult.Saved, await f.Service.DeleteManuallyAsync(t1, None));

        Assert.Empty(f.Service.ReadConsumables());
        Assert.Empty(f.Service.ReadStatuses().Stations.Single().Items);
        Assert.Equal(0, f.Service.ReadStatuses().Devices.Single().Slots.Delivering);
    }
}
