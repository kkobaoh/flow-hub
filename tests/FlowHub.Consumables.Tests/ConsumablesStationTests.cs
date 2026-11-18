using FlowHub.BuildingBlocks;
using FlowHub.Consumables.Application;
using FlowHub.Dispatching;
using FlowHub.Map;

namespace FlowHub.Consumables.Tests;

/// <summary>
/// ステーションまわり（ID の読み取りによる入庫、要確認の修正、出庫）のテスト。
/// </summary>
public class ConsumablesStationTests
{
    private static readonly DeviceId Device = new("D1");
    private static readonly StationId Station = new("S1");
    private static readonly ProcessTypeId Process = new("P");
    private static readonly VarietyId VarietyA = new("A");
    private static readonly ConsumableId C1 = new("C-1");
    private static readonly CancellationToken None = CancellationToken.None;

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
    }

    private sealed class NoDispatching : IDispatchingService
    {
        public Task<TransportId> RequestTransportAsync(TransportOrder order, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> CancelTransportAsync(TransportId id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ReportTransferCompletedAsync(TransportId id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoDevices : IDeviceGateway
    {
        public Task GrantTransferAsync(DeviceId device, TransportId transport, TransferSide side, IReadOnlyList<CargoSlot> cargo, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class RecordingStations : IStationGateway
    {
        public List<(StationId Station, ConsumableId Consumable)> Shipments { get; } = [];

        public Task GrantTransferAsync(StationId station, TransportId transport, TransferSide side, IReadOnlyList<ConsumableId> cargo, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RequestShipmentAsync(StationId station, ConsumableId consumable, CancellationToken cancellationToken)
        {
            Shipments.Add((station, consumable));
            return Task.CompletedTask;
        }
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

    private sealed record Fixture(ConsumablesService Service, RecordingStations Stations, RecordingPublisher Publisher);

    /// <summary>
    /// 装置 D1（スロット3つ）と、ステーション S1。品種 A は加工の種類 P。
    /// </summary>
    private static Fixture Setup()
    {
        var stations = new RecordingStations();
        var publisher = new RecordingPublisher();
        var service = new ConsumablesService(new NoDispatching(), new NoDevices(), stations, new IgnoringReplenishment(), publisher, new FixedTimeProvider());
        service.RegisterDevice(new DeviceLayout(Device, new PanelId("D"), SlotsPerStocker: [3]));
        service.RegisterStation(new StationLayout(Station, new PanelId("S")));
        service.RegisterProcessType(new ProcessTypeSettings(Process, new CarrierTypeId("X"), CarrierCapacity: 2));
        service.RegisterVariety(VarietyA, Process);
        return new Fixture(service, stations, publisher);
    }

    // ---- ID の読み取りによる入庫 ----

    [Fact]
    public async Task 前もって品種を登録したIDを読み取ったらその品種の新品として入庫する()
    {
        var f = Setup();
        f.Service.RegisterExpectedArrival(C1, VarietyA);

        var report = await f.Service.ReportReadAtStationAsync(Station, C1, None);

        Assert.Equal(new ConsumableView(C1, VarietyA, Process, ConsumableCondition.New, new AtStation(Station)), f.Service.GetConsumable(C1));
        Assert.Equal([new StationStock(C1, Station)], f.Service.GetStationStock(VarietyA));
        Assert.Empty(report.Mismatched);
    }

    [Fact]
    public async Task 品種の対応がないIDは品種のわからない要確認の消耗品として登録する()
    {
        var f = Setup();

        await f.Service.ReportReadAtStationAsync(Station, C1, None);

        Assert.Equal(new ConsumableView(C1, null, null, ConsumableCondition.New, new AtStation(Station), NeedsCheck: true), f.Service.GetConsumable(C1));
    }

    [Fact]
    public async Task 所在不明だった消耗品は所在をステーションに確定させ要確認にする()
    {
        var f = Setup();
        f.Service.RegisterConsumable(C1, VarietyA, Process, ConsumableCondition.New, new UnknownLocation());

        var report = await f.Service.ReportReadAtStationAsync(Station, C1, None);

        var view = f.Service.GetConsumable(C1);
        Assert.Equal(new AtStation(Station), view.Location);
        Assert.True(view.NeedsCheck);
        Assert.Empty(f.Service.GetStationStock(VarietyA));
        Assert.Empty(report.Mismatched);
    }

    [Fact]
    public async Task ほかの場所にあることになっていた消耗品は通知を正としてステーションに置き予約を解いて食い違いを返す()
    {
        // C-1 は D1 へ届ける途中（AGV の中）のはずだった
        var f = Setup();
        f.Service.RegisterConsumable(C1, VarietyA, Process, ConsumableCondition.New, new OnAgv(new TransportId(1)));
        await f.Service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 1 }, None);
        f.Service.ReserveForDelivery(Device, [C1]);

        var report = await f.Service.ReportReadAtStationAsync(Station, C1, None);

        Assert.Equal(new AtStation(Station), f.Service.GetConsumable(C1).Location);
        Assert.Equal([C1], report.Mismatched);
        Assert.Equal(new Dictionary<VarietyId, int> { [VarietyA] = 1 }, f.Service.EvaluateDevice(Device).Shortages);
    }

    [Fact]
    public async Task 同じステーションで同じIDをもう一度読み取っても変わらない()
    {
        var f = Setup();
        f.Service.RegisterExpectedArrival(C1, VarietyA);
        await f.Service.ReportReadAtStationAsync(Station, C1, None);

        var report = await f.Service.ReportReadAtStationAsync(Station, C1, None);

        Assert.Empty(report.Mismatched);
        Assert.Equal(new ConsumableView(C1, VarietyA, Process, ConsumableCondition.New, new AtStation(Station)), f.Service.GetConsumable(C1));
    }

    [Fact]
    public void 品種の対応は登録済みの消耗品や同じIDに二重に登録できない()
    {
        var f = Setup();
        f.Service.RegisterExpectedArrival(C1, VarietyA);
        f.Service.RegisterConsumable(new ConsumableId("C-2"), VarietyA, Process, ConsumableCondition.New, new AtStation(Station));

        Assert.Throws<InvalidOperationException>(() => f.Service.RegisterExpectedArrival(C1, VarietyA));
        Assert.Throws<InvalidOperationException>(() => f.Service.RegisterExpectedArrival(new ConsumableId("C-2"), VarietyA));
        Assert.Throws<InvalidOperationException>(() => f.Service.RegisterExpectedArrival(new ConsumableId("C-3"), new VarietyId("Q")));
    }

    // ---- 要確認の修正 ----

    [Fact]
    public async Task 要確認の消耗品の品種と状態を直すと要確認が外れて届ける候補になる()
    {
        var f = Setup();
        await f.Service.ReportReadAtStationAsync(Station, C1, None);

        await f.Service.CorrectAtStationAsync(C1, VarietyA, ConsumableCondition.New, None);

        Assert.Equal(new ConsumableView(C1, VarietyA, Process, ConsumableCondition.New, new AtStation(Station)), f.Service.GetConsumable(C1));
        Assert.Equal([new StationStock(C1, Station)], f.Service.GetStationStock(VarietyA));
    }

    [Fact]
    public async Task 中古品と直した消耗品は届ける候補にならない()
    {
        var f = Setup();
        await f.Service.ReportReadAtStationAsync(Station, C1, None);

        await f.Service.CorrectAtStationAsync(C1, VarietyA, ConsumableCondition.Used, None);

        Assert.False(f.Service.GetConsumable(C1).NeedsCheck);
        Assert.Empty(f.Service.GetStationStock(VarietyA));
    }

    [Fact]
    public async Task ステーションにない消耗品や要確認でない消耗品は直せない()
    {
        var f = Setup();
        f.Service.RegisterConsumable(C1, VarietyA, Process, ConsumableCondition.New, new AtStation(Station));
        var lost = new ConsumableId("C-2");
        f.Service.RegisterConsumable(lost, VarietyA, Process, ConsumableCondition.New, new UnknownLocation());

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.CorrectAtStationAsync(C1, VarietyA, ConsumableCondition.Used, None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.CorrectAtStationAsync(lost, VarietyA, ConsumableCondition.Used, None));
    }

    // ---- 出庫 ----

    [Fact]
    public async Task 出庫の知らせで記録から消し予約を解く()
    {
        var f = Setup();
        f.Service.RegisterConsumable(C1, VarietyA, Process, ConsumableCondition.New, new AtStation(Station));
        await f.Service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 1 }, None);
        f.Service.ReserveForDelivery(Device, [C1]);

        var report = await f.Service.ReportShippedFromStationAsync(Station, C1, None);

        Assert.Throws<InvalidOperationException>(() => f.Service.GetConsumable(C1));
        Assert.Equal(new Dictionary<VarietyId, int> { [VarietyA] = 1 }, f.Service.EvaluateDevice(Device).Shortages);
        Assert.Empty(report.Mismatched);
    }

    [Fact]
    public async Task 記録上そのステーションにない消耗品の出庫の知らせは通知を正として消し食い違いを返す()
    {
        var f = Setup();
        f.Service.RegisterConsumable(C1, VarietyA, Process, ConsumableCondition.New, new UnknownLocation());

        var report = await f.Service.ReportShippedFromStationAsync(Station, C1, None);

        Assert.Throws<InvalidOperationException>(() => f.Service.GetConsumable(C1));
        Assert.Equal([C1], report.Mismatched);
    }

    [Fact]
    public async Task 未登録のIDの出庫の知らせは食い違いとして返す()
    {
        var f = Setup();

        var report = await f.Service.ReportShippedFromStationAsync(Station, C1, None);

        Assert.Equal([C1], report.Mismatched);
    }

    [Fact]
    public async Task 出庫を頼むとステーションに頼み出るまで届ける候補から外す()
    {
        var f = Setup();
        f.Service.RegisterConsumable(C1, VarietyA, Process, ConsumableCondition.New, new AtStation(Station));

        var accepted = await f.Service.RequestShipmentAsync(C1, None);

        Assert.True(accepted);
        Assert.Equal([(Station, C1)], f.Stations.Shipments);
        Assert.Empty(f.Service.GetStationStock(VarietyA));
    }

    [Fact]
    public async Task ステーションにない消耗品の出庫は受け付けない()
    {
        var f = Setup();
        f.Service.RegisterConsumable(C1, VarietyA, Process, ConsumableCondition.New, new InStocker(Device, new SlotRef(1, 1)));

        var accepted = await f.Service.RequestShipmentAsync(C1, None);

        Assert.False(accepted);
        Assert.Empty(f.Stations.Shipments);
    }

    // ---- 変化の知らせ ----

    [Fact]
    public async Task 読み取りと修正と出庫の知らせで在庫が変わったと知らせ出庫を頼んだだけでは知らせない()
    {
        var f = Setup();
        f.Service.RegisterConsumable(new ConsumableId("C-2"), VarietyA, Process, ConsumableCondition.New, new AtStation(Station));

        await f.Service.ReportReadAtStationAsync(Station, C1, None);
        await f.Service.CorrectAtStationAsync(C1, VarietyA, ConsumableCondition.New, None);
        await f.Service.RequestShipmentAsync(C1, None);
        await f.Service.ReportShippedFromStationAsync(Station, C1, None);

        Assert.Equal(3, f.Publisher.Published.OfType<StockChanged>().Count());
    }
}
