using FlowHub.BuildingBlocks;
using FlowHub.Consumables.Application;
using FlowHub.Consumables.Domain;
using FlowHub.Dispatching;
using FlowHub.Map;

namespace FlowHub.Consumables.Tests;

/// <summary>
/// 搬送計画が使う口（判断、ステーションの在庫、予約、補充、変化の知らせ）のテスト。
/// </summary>
public class ConsumablesPlanningTests
{
    private static readonly DeviceId Device = new("D1");
    private static readonly StationId Station = new("S1");
    private static readonly ProcessTypeId Process = new("P");
    private static readonly VarietyId VarietyA = new("A");
    private static readonly VarietyId VarietyB = new("B");
    private static readonly VarietyId VarietyZ = new("Z");
    private static readonly TransportId Transport1 = new(1);
    private static readonly CancellationToken None = CancellationToken.None;

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
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

    private sealed class RecordingReplenishment : IUpstreamGateway
    {
        public List<(VarietyId Variety, int Quantity)> Requests { get; } = [];

        public Task RequestReplenishmentAsync(VarietyId variety, int quantity, CancellationToken cancellationToken)
        {
            Requests.Add((variety, quantity));
            return Task.CompletedTask;
        }

        public Task NotifySetupErrorAsync(DeviceId device, VarietyId variety, CancellationToken cancellationToken) => Task.CompletedTask;
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

    private sealed record Fixture(ConsumablesService Service, RecordingReplenishment Replenishment, RecordingPublisher Publisher);

    /// <summary>
    /// 装置 D1（ストッカー1つ、スロット3つ）と、ステーション S1。品種 A、B、Z は加工の種類 P。設定はまだない。
    /// </summary>
    private static Fixture Setup()
    {
        var replenishment = new RecordingReplenishment();
        var publisher = new RecordingPublisher();
        var service = new ConsumablesService(new NoDispatching(), new NoDevices(), new NoStations(), replenishment, publisher, new FixedTimeProvider());
        service.RegisterDevice(new DeviceLayout(Device, new PanelId("D"), SlotsPerStocker: [3]));
        service.RegisterStation(new StationLayout(Station, new PanelId("S")));
        service.RegisterProcessType(new ProcessTypeSettings(Process, new CarrierTypeId("X"), CarrierCapacity: 2));
        service.RegisterVariety(VarietyA, Process);
        service.RegisterVariety(VarietyB, Process);
        service.RegisterVariety(VarietyZ, Process);
        return new Fixture(service, replenishment, publisher);
    }

    private static ConsumableId InStocker(Fixture f, string id, VarietyId variety, int slot, ConsumableCondition condition = ConsumableCondition.New)
    {
        var consumable = new ConsumableId(id);
        f.Service.RegisterConsumable(consumable, variety, Process, condition, new InStocker(Device, new SlotRef(1, slot)));
        return consumable;
    }

    private static ConsumableId AtStation(Fixture f, string id, VarietyId variety)
    {
        var consumable = new ConsumableId(id);
        f.Service.RegisterConsumable(consumable, variety, Process, ConsumableCondition.New, new AtStation(Station));
        return consumable;
    }

    private static TransferPermissionRequested Permission(TransportId transport, string endpoint, TransferSide side, params ConsumableId[] cargo) =>
        new(EventMetadata.Start("test", new FixedTimeProvider()), transport, ConsumablesService.Requester, cargo.Select(c => c.Value).ToArray(), endpoint, side);

    // ---- 判断 ----

    [Fact]
    public async Task 設定と在庫を比べて不足を返す()
    {
        var f = Setup();
        InStocker(f, "C-1", VarietyA, 1);

        await f.Service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 2, [VarietyB] = 1 }, None);
        var needs = f.Service.EvaluateDevice(Device);

        Assert.Equal(new Dictionary<VarietyId, int> { [VarietyA] = 1, [VarietyB] = 1 }, needs.Shortages);
        Assert.Empty(needs.Recoveries);
    }

    [Fact]
    public void 設定がまだない装置は不足も回収もない()
    {
        var f = Setup();
        InStocker(f, "C-1", VarietyA, 1);

        var needs = f.Service.EvaluateDevice(Device);

        Assert.Empty(needs.Shortages);
        Assert.Empty(needs.Recoveries);
    }

    [Fact]
    public async Task 設定にない品種の新品は回収として品種と加工の種類を付けて返す()
    {
        var f = Setup();
        var z = InStocker(f, "C-Z", VarietyZ, 1);

        await f.Service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 1 }, None);

        Assert.Equal([new DeviceRecovery(z, VarietyZ, Process, RecoveryReason.NotInSetting)], f.Service.EvaluateDevice(Device).Recoveries);
    }

    [Fact]
    public async Task 届ける予約をした消耗品は届ける途中として不足に数える()
    {
        var f = Setup();
        var a = AtStation(f, "C-1", VarietyA);
        await f.Service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 1 }, None);

        f.Service.ReserveForDelivery(Device, [a]);

        Assert.Empty(f.Service.EvaluateDevice(Device).Shortages);
    }

    [Fact]
    public async Task 予約を解いたら不足に戻る()
    {
        var f = Setup();
        var a = AtStation(f, "C-1", VarietyA);
        await f.Service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 1 }, None);
        f.Service.ReserveForDelivery(Device, [a]);

        f.Service.ReleaseReservations([a]);

        Assert.Equal(new Dictionary<VarietyId, int> { [VarietyA] = 1 }, f.Service.EvaluateDevice(Device).Shortages);
    }

    [Fact]
    public async Task 回収の予約をした消耗品はもう一度回収を頼まない()
    {
        var f = Setup();
        var z = InStocker(f, "C-Z", VarietyZ, 1);
        await f.Service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 1 }, None);

        f.Service.ReserveForRecovery([z]);

        Assert.Empty(f.Service.EvaluateDevice(Device).Recoveries);
    }

    [Fact]
    public async Task 廃棄リクエストを受けた中古品は廃棄として回収を返す()
    {
        var f = Setup();
        var used = InStocker(f, "C-U", VarietyA, 1, ConsumableCondition.Used);
        await f.Service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 0 }, None);

        await f.Service.RequestDiscardAsync(Device, used, None);

        Assert.Equal([new DeviceRecovery(used, VarietyA, Process, RecoveryReason.Discard)], f.Service.EvaluateDevice(Device).Recoveries);
    }

    [Fact]
    public async Task ストッカーにない消耗品の廃棄リクエストは受け付けない()
    {
        var f = Setup();
        var a = AtStation(f, "C-1", VarietyA);

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.RequestDiscardAsync(Device, a, None));
    }

    // ---- 空き ----

    [Fact]
    public async Task 空きは今空いているスロットから届ける途中の数を引いた数()
    {
        // スロット3つのうち、C-1 が1つふさぎ、C-2 を届ける途中
        var f = Setup();
        InStocker(f, "C-1", VarietyA, 1);
        var b = AtStation(f, "C-2", VarietyB);
        await f.Service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 1, [VarietyB] = 2 }, None);

        f.Service.ReserveForDelivery(Device, [b]);

        Assert.Equal(1, f.Service.EvaluateDevice(Device).FreeSlots);
    }

    [Fact]
    public async Task 回収を頼んだだけでまだ取り出していない消耗品はスロットをふさぐ()
    {
        var f = Setup();
        var z = InStocker(f, "C-Z", VarietyZ, 1);
        await f.Service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 1 }, None);

        f.Service.ReserveForRecovery([z]);

        Assert.Equal(2, f.Service.EvaluateDevice(Device).FreeSlots);
    }

    [Fact]
    public async Task 降ろした消耗品は届ける途中から外れて受け渡し部の分としてスロットをふさぐ()
    {
        var f = Setup();
        var a = AtStation(f, "C-1", VarietyA);
        await f.Service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 1 }, None);
        f.Service.ReserveForDelivery(Device, [a]);
        await f.Service.OnTransferPermissionRequestedAsync(Permission(Transport1, Endpoints.Device(Device), TransferSide.Unload, a), None);

        await f.Service.ReportTransferCompletedAsync(Transport1, [a], None);
        f.Service.ReleaseReservations([a]); // 予約はもうないので、解いても変わらない

        var needs = f.Service.EvaluateDevice(Device);
        Assert.Empty(needs.Shortages);
        Assert.Equal(2, needs.FreeSlots);
    }

    // ---- ステーションの在庫 ----

    [Fact]
    public void ステーションの在庫は入庫した順に予約していない新品だけを返す()
    {
        var f = Setup();
        var first = AtStation(f, "C-9", VarietyA);
        var second = AtStation(f, "C-1", VarietyA);
        var reserved = AtStation(f, "C-5", VarietyA);
        AtStation(f, "C-B", VarietyB);
        f.Service.ReserveForDelivery(Device, [reserved]);

        Assert.Equal([new StationStock(first, Station), new StationStock(second, Station)], f.Service.GetStationStock(VarietyA));
    }

    [Fact]
    public async Task ステーションに返した消耗品はそのときに入庫したものとして後ろに並ぶ()
    {
        var f = Setup();
        var z = InStocker(f, "C-Z", VarietyA, 1);
        var a = AtStation(f, "C-1", VarietyA);
        await f.Service.OnTransferPermissionRequestedAsync(Permission(Transport1, Endpoints.Device(Device), TransferSide.Load, z), None);
        await f.Service.ReportTransferCompletedAsync(Transport1, [z], None);
        await f.Service.OnTransferPermissionRequestedAsync(Permission(Transport1, Endpoints.Station(Station), TransferSide.Unload, z), None);

        await f.Service.ReportTransferCompletedAsync(Transport1, [z], None);

        Assert.Equal([a, z], f.Service.GetStationStock(VarietyA).Select(s => s.Consumable));
    }

    [Fact]
    public async Task 入庫した消耗品はステーションの在庫に入る()
    {
        var f = Setup();
        var a = new ConsumableId("C-1");

        f.Service.RegisterExpectedArrival(a, VarietyA);

        await f.Service.ReportReadAtStationAsync(Station, a, None);

        Assert.Equal([new StationStock(a, Station)], f.Service.GetStationStock(VarietyA));
        Assert.Equal(Process, f.Service.GetConsumable(a).ProcessType);
    }

    // ---- 補充 ----

    [Fact]
    public async Task 補充を頼むと上位システムに頼む()
    {
        var f = Setup();

        await f.Service.RequestReplenishmentAsync(VarietyA, 2, None);

        Assert.Equal([(VarietyA, 2)], f.Replenishment.Requests);
    }

    [Fact]
    public async Task 頼んでまだ届いていない数を引いたぶんだけ頼む()
    {
        // A は2個頼んだあと、足りない数が3個に増えたので1個だけ足す。足りない数が2個に戻っても頼まない
        var f = Setup();
        await f.Service.RequestReplenishmentAsync(VarietyA, 2, None);

        await f.Service.RequestReplenishmentAsync(VarietyA, 3, None);
        await f.Service.RequestReplenishmentAsync(VarietyA, 2, None);
        await f.Service.RequestReplenishmentAsync(VarietyB, 1, None);

        Assert.Equal([(VarietyA, 2), (VarietyA, 1), (VarietyB, 1)], f.Replenishment.Requests);
    }

    [Fact]
    public async Task 入庫したらその品種の届いていない数を減らす()
    {
        var f = Setup();
        await f.Service.RequestReplenishmentAsync(VarietyA, 2, None);
        f.Service.RegisterExpectedArrival(new ConsumableId("C-1"), VarietyA);
        await f.Service.ReportReadAtStationAsync(Station, new ConsumableId("C-1"), None);

        await f.Service.RequestReplenishmentAsync(VarietyA, 2, None);

        Assert.Equal([(VarietyA, 2), (VarietyA, 1)], f.Replenishment.Requests);
    }

    [Fact]
    public async Task 未登録のIDとして入庫した消耗品を新品と直したら入庫に数える()
    {
        // 品種の対応を登録しないまま届いた補充とみなす
        var f = Setup();
        await f.Service.RequestReplenishmentAsync(VarietyA, 2, None);
        await f.Service.ReportReadAtStationAsync(Station, new ConsumableId("X-1"), None);
        await f.Service.CorrectAtStationAsync(new ConsumableId("X-1"), VarietyA, ConsumableCondition.New, None);

        await f.Service.RequestReplenishmentAsync(VarietyA, 2, None);

        Assert.Equal([(VarietyA, 2), (VarietyA, 1)], f.Replenishment.Requests);
    }

    [Fact]
    public async Task 登録済みだった消耗品を直しても入庫に数えない()
    {
        // 所在不明だった C-9 がステーションで見つかった。前からあった消耗品なので、補充ではない
        var f = Setup();
        var c9 = new ConsumableId("C-9");
        f.Service.RegisterConsumable(c9, VarietyA, Process, ConsumableCondition.New, new UnknownLocation());
        await f.Service.RequestReplenishmentAsync(VarietyA, 2, None);
        await f.Service.ReportReadAtStationAsync(Station, c9, None);
        await f.Service.CorrectAtStationAsync(c9, VarietyA, ConsumableCondition.New, None);

        await f.Service.RequestReplenishmentAsync(VarietyA, 2, None);

        Assert.Equal([(VarietyA, 2)], f.Replenishment.Requests);
    }

    [Fact]
    public async Task 未登録のIDとして入庫した消耗品でも中古品と直したら入庫に数えない()
    {
        var f = Setup();
        await f.Service.RequestReplenishmentAsync(VarietyA, 2, None);
        await f.Service.ReportReadAtStationAsync(Station, new ConsumableId("X-1"), None);
        await f.Service.CorrectAtStationAsync(new ConsumableId("X-1"), VarietyA, ConsumableCondition.Used, None);

        await f.Service.RequestReplenishmentAsync(VarietyA, 2, None);

        Assert.Equal([(VarietyA, 2)], f.Replenishment.Requests);
    }

    // ---- 変化の知らせ ----

    [Fact]
    public async Task 設定の変更と入庫と廃棄リクエストと受け渡しの完了で在庫が変わったと知らせる()
    {
        var f = Setup();
        var used = InStocker(f, "C-U", VarietyA, 1, ConsumableCondition.Used);
        var a = AtStation(f, "C-1", VarietyA);

        await f.Service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 1 }, None);
        f.Service.RegisterExpectedArrival(new ConsumableId("C-2"), VarietyA);
        await f.Service.ReportReadAtStationAsync(Station, new ConsumableId("C-2"), None);
        await f.Service.RequestDiscardAsync(Device, used, None);
        await f.Service.OnTransferPermissionRequestedAsync(Permission(Transport1, Endpoints.Station(Station), TransferSide.Load, a), None);
        await f.Service.ReportTransferCompletedAsync(Transport1, [a], None);

        Assert.Equal(4, f.Publisher.Published.OfType<StockChanged>().Count());
    }

    [Fact]
    public void 予約と予約の解除では知らせない()
    {
        var f = Setup();
        var a = AtStation(f, "C-1", VarietyA);

        f.Service.ReserveForDelivery(Device, [a]);
        f.Service.ReleaseReservations([a]);

        Assert.Empty(f.Publisher.Published);
    }

    // ---- 搬送計画が使う情報 ----

    [Fact]
    public void 装置とステーションの受け渡しの相手と品種の加工の種類を返す()
    {
        var f = Setup();

        Assert.Equal([Device], f.Service.Devices);
        Assert.Equal([Station], f.Service.Stations);
        Assert.Equal(new TransferEndpoint(new PanelId("D"), Endpoints.Device(Device)), f.Service.DeviceEndpoint(Device));
        Assert.Equal(new TransferEndpoint(new PanelId("S"), Endpoints.Station(Station)), f.Service.StationEndpoint(Station));
        Assert.Equal(Process, f.Service.ProcessTypeOf(VarietyA).Id);
    }

    [Fact]
    public async Task 登録されていない品種は設定に使えない()
    {
        var f = Setup();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [new VarietyId("Q")] = 1 }, None));
    }
}
