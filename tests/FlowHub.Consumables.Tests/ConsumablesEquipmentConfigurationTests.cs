using FlowHub.BuildingBlocks;
using FlowHub.Consumables.Application;
using FlowHub.Dispatching;
using FlowHub.Map;

namespace FlowHub.Consumables.Tests;

/// <summary>
/// 消耗品の構成のうち、装置とステーションを、動いている間に加える・変える・消すテスト。
/// </summary>
public class ConsumablesEquipmentConfigurationTests
{
    private static readonly DeviceId Device = new("D1");
    private static readonly StationId Station = new("S1");
    private static readonly ProcessTypeId Process = new("P");
    private static readonly VarietyId VarietyA = new("A");
    private static readonly CancellationToken None = CancellationToken.None;

    /// <summary>公開中のマップの受け渡しパネル。</summary>
    private static readonly IReadOnlySet<PanelId> Panels = new HashSet<PanelId> { new("PD1"), new("PS1"), new("PD2"), new("PS2") };

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

    /// <summary>保存を頼まれたものを、文で残す。</summary>
    private sealed class RecordingPersistence : IConsumablesPersistence
    {
        public List<string> Calls { get; } = [];

        public void SaveStockerSetting(DeviceId device, IReadOnlyDictionary<VarietyId, int> counts) => Calls.Add($"setting {device}");

        public void AddExpectedArrival(ConsumableId consumable, VarietyId variety) => Calls.Add($"arrival+ {consumable}");

        public void RemoveExpectedArrival(ConsumableId consumable) => Calls.Add($"arrival- {consumable}");

        public void SaveProcessType(ProcessTypeSettings settings) => Calls.Add($"process {settings.Id}");

        public void DeleteProcessType(ProcessTypeId processType) => Calls.Add($"process- {processType}");

        public void SaveVariety(VarietyId variety, ProcessTypeId processType) => Calls.Add($"variety {variety}");

        public void DeleteVariety(VarietyId variety) => Calls.Add($"variety- {variety}");

        public void SaveDevice(DeviceLayout layout) => Calls.Add($"device {layout.Id} {layout.TransferPanel} {string.Join(",", layout.SlotsPerStocker)}");

        public void DeleteDevice(DeviceId device) => Calls.Add($"device- {device}");

        public void SaveStation(StationLayout layout) => Calls.Add($"station {layout.Id} {layout.TransferPanel}");

        public void DeleteStation(StationId station) => Calls.Add($"station- {station}");
    }

    private sealed record Fixture(ConsumablesService Service, RecordingPersistence Saved, RecordingPublisher Events);

    /// <summary>装置 D1（受け渡しパネル PD1、ストッカー2つ、スロット 2 と 2）、ステーション S1（PS1）。品種 A は加工の種類 P。</summary>
    private static Fixture Setup()
    {
        var saved = new RecordingPersistence();
        var events = new RecordingPublisher();
        var service = new ConsumablesService(new NoDispatching(), new NoDevices(), new NoStations(), new IgnoringReplenishment(), events, TimeProvider.System, saved);
        service.RegisterDevice(new DeviceLayout(Device, new PanelId("PD1"), SlotsPerStocker: [2, 2]));
        service.RegisterStation(new StationLayout(Station, new PanelId("PS1")));
        service.RegisterProcessType(new ProcessTypeSettings(Process, new CarrierTypeId("X"), CarrierCapacity: 2));
        service.RegisterVariety(VarietyA, Process);
        return new Fixture(service, saved, events);
    }

    private static DeviceLayout Layout(string id, string panel, params int[] slots) => new(new DeviceId(id), new PanelId(panel), slots);

    private static Task<ManualEntryResult> PutAsync(Fixture f, string id, ConsumableLocation location) =>
        f.Service.RegisterManuallyAsync(new ConsumableId(id), VarietyA, ConsumableCondition.New, location, None);

    // ---- 装置 ----

    [Fact]
    public async Task 装置を加えると保存し判断の対象になる()
    {
        var f = Setup();

        var result = await f.Service.AddDeviceAsync(Layout("D2", "PD2", 3), Panels, None);

        Assert.Equal(ConfigurationChangeResult.Saved, result);
        Assert.Equal(["device D2 PD2 3"], f.Saved.Calls);
        Assert.Contains(new DeviceId("D2"), f.Service.Devices);
        Assert.Equal(3, f.Service.EvaluateDevice(new DeviceId("D2")).FreeSlots);
        Assert.Single(f.Events.Published.OfType<StockChanged>());
    }

    [Fact]
    public async Task 同じIDの装置は加えるのを断る()
    {
        var f = Setup();

        Assert.Equal(ConfigurationChangeResult.AlreadyRegistered, await f.Service.AddDeviceAsync(Layout("D1", "PD2", 3), Panels, None));
        Assert.Empty(f.Saved.Calls);
    }

    [Fact]
    public async Task マップの受け渡しパネルでないかほかが使っているパネルは断る()
    {
        var f = Setup();

        Assert.Equal(ConfigurationChangeResult.PanelUnavailable, await f.Service.AddDeviceAsync(Layout("D2", "N1", 3), Panels, None));
        Assert.Equal(ConfigurationChangeResult.PanelUnavailable, await f.Service.AddDeviceAsync(Layout("D2", "PS1", 3), Panels, None));
        Assert.Equal(ConfigurationChangeResult.PanelUnavailable, await f.Service.UpdateStationAsync(new StationLayout(Station, new PanelId("PD1")), Panels, None));
        Assert.Equal(ConfigurationChangeResult.PanelUnavailable, await f.Service.UpdateDeviceAsync(Layout("D1", "PS1", 2, 2), Panels, None));
        Assert.Empty(f.Saved.Calls);
    }

    [Fact]
    public async Task ストッカーのない装置は頼みの誤り()
    {
        var f = Setup();

        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.AddDeviceAsync(Layout("D2", "PD2"), Panels, None));
    }

    [Fact]
    public async Task 使われていない装置は受け渡しパネルとスロットの数を変えられる()
    {
        var f = Setup();

        var result = await f.Service.UpdateDeviceAsync(Layout("D1", "PD2", 3), Panels, None);

        Assert.Equal(ConfigurationChangeResult.Saved, result);
        Assert.Equal(["device D1 PD2 3"], f.Saved.Calls);
        Assert.Equal(new PanelId("PD2"), f.Service.DeviceEndpoint(Device).Panel);
    }

    [Fact]
    public async Task 登録していない装置は変えるのも消すのも断る()
    {
        var f = Setup();

        Assert.Equal(ConfigurationChangeResult.NotFound, await f.Service.UpdateDeviceAsync(Layout("D9", "PD2", 1), Panels, None));
        Assert.Equal(ConfigurationChangeResult.NotFound, await f.Service.DeleteDeviceAsync(new DeviceId("D9"), None));
    }

    [Fact]
    public async Task 消耗品のある装置は受け渡しパネルを変えるのも消すのも断るがスロットは増やせる()
    {
        var f = Setup();
        Assert.Equal(ManualEntryResult.Saved, await PutAsync(f, "C1", new InStocker(Device, new SlotRef(1, 1))));
        f.Saved.Calls.Clear();

        Assert.Equal(ConfigurationChangeResult.InUse, await f.Service.UpdateDeviceAsync(Layout("D1", "PD2", 2, 2), Panels, None));
        Assert.Equal(ConfigurationChangeResult.InUse, await f.Service.DeleteDeviceAsync(Device, None));
        Assert.Equal(ConfigurationChangeResult.Saved, await f.Service.UpdateDeviceAsync(Layout("D1", "PD1", 2, 3), Panels, None));
        Assert.Equal(["device D1 PD1 2,3"], f.Saved.Calls);
    }

    [Fact]
    public async Task 届ける予約か指名のある装置は使われているものとして消すのを断る()
    {
        var f = Setup();
        Assert.Equal(ManualEntryResult.Saved, await PutAsync(f, "C1", new AtStation(Station)));
        Assert.Equal(ManualEntryResult.Saved, await PutAsync(f, "C2", new AtStation(Station)));

        f.Service.ReserveForDelivery(Device, [new ConsumableId("C1")]);
        Assert.Equal(ConfigurationChangeResult.InUse, await f.Service.DeleteDeviceAsync(Device, None));
        f.Service.ReleaseReservations([new ConsumableId("C1")]);

        Assert.Equal(DesignationResult.Designated, f.Service.Designate(Device, new ConsumableId("C2")));
        Assert.Equal(ConfigurationChangeResult.InUse, await f.Service.DeleteDeviceAsync(Device, None));
        f.Service.CancelDesignation(new ConsumableId("C2"));

        Assert.Equal(ConfigurationChangeResult.Saved, await f.Service.DeleteDeviceAsync(Device, None));
    }

    [Fact]
    public async Task 加工中の消耗品の元のスロットがあるストッカーは減らせない()
    {
        var f = Setup();
        Assert.Equal(ManualEntryResult.Saved, await PutAsync(f, "C1", new InStocker(Device, new SlotRef(2, 2))));
        await f.Service.ReportTakenIntoProcessingAsync(Device, new ConsumableId("C1"), None);
        f.Saved.Calls.Clear();

        Assert.Equal(ConfigurationChangeResult.SlotsOccupied, await f.Service.UpdateDeviceAsync(Layout("D1", "PD1", 2, 1), Panels, None));
        Assert.Equal(ConfigurationChangeResult.SlotsOccupied, await f.Service.UpdateDeviceAsync(Layout("D1", "PD1", 2), Panels, None));
        Assert.Equal(ConfigurationChangeResult.Saved, await f.Service.UpdateDeviceAsync(Layout("D1", "PD1", 1, 2), Panels, None));
    }

    [Fact]
    public async Task スロットを減らしてストッカーの設定の合計を下回るときは断る()
    {
        var f = Setup();
        await f.Service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 3 }, None);
        f.Saved.Calls.Clear();

        Assert.Equal(ConfigurationChangeResult.ExceedsStockerSetting, await f.Service.UpdateDeviceAsync(Layout("D1", "PD1", 2), Panels, None));
        Assert.Equal(ConfigurationChangeResult.Saved, await f.Service.UpdateDeviceAsync(Layout("D1", "PD1", 3), Panels, None));
    }

    [Fact]
    public async Task 装置を消すとストッカーの設定も消え判断の対象から外れる()
    {
        var f = Setup();
        await f.Service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 1 }, None);
        f.Saved.Calls.Clear();

        Assert.Equal(ConfigurationChangeResult.Saved, await f.Service.DeleteDeviceAsync(Device, None));

        Assert.Equal(["device- D1"], f.Saved.Calls);
        Assert.Empty(f.Service.Devices);
        Assert.False(f.Service.ReadConfiguration().Varieties.Single().InUse);
        Assert.Equal(ConfigurationChangeResult.Saved, await f.Service.AddDeviceAsync(Layout("D2", "PD1", 1), Panels, None)); // パネルが空いた
    }

    // ---- ステーション ----

    [Fact]
    public async Task ステーションは1台だけで2台目は断る()
    {
        var f = Setup();

        var result = await f.Service.AddStationAsync(new StationLayout(new StationId("S2"), new PanelId("PS2")), Panels, None);

        Assert.Equal(ConfigurationChangeResult.StationLimit, result);
        Assert.Empty(f.Saved.Calls);
    }

    [Fact]
    public async Task 使われていないステーションは受け渡しパネルを変えて消して加え直せる()
    {
        var f = Setup();

        Assert.Equal(ConfigurationChangeResult.Saved, await f.Service.UpdateStationAsync(new StationLayout(Station, new PanelId("PS2")), Panels, None));
        Assert.Equal(new PanelId("PS2"), f.Service.StationEndpoint(Station).Panel);
        Assert.Equal(ConfigurationChangeResult.Saved, await f.Service.DeleteStationAsync(Station, None));
        Assert.Empty(f.Service.Stations);
        Assert.Equal(ConfigurationChangeResult.Saved, await f.Service.AddStationAsync(new StationLayout(new StationId("S2"), new PanelId("PS1")), Panels, None));

        Assert.Equal(["station S1 PS2", "station- S1", "station S2 PS1"], f.Saved.Calls);
    }

    [Fact]
    public async Task 消耗品か届ける予約のあるステーションは受け渡しパネルを変えるのも消すのも断る()
    {
        var f = Setup();
        Assert.Equal(ManualEntryResult.Saved, await PutAsync(f, "C1", new AtStation(Station)));

        Assert.Equal(ConfigurationChangeResult.InUse, await f.Service.UpdateStationAsync(new StationLayout(Station, new PanelId("PS2")), Panels, None));
        Assert.Equal(ConfigurationChangeResult.InUse, await f.Service.DeleteStationAsync(Station, None));

        // 装置のストッカーへ移し、回収の予約が残っているあいだも、運び先のステーションは使われている
        Assert.Equal(ManualEntryResult.Saved, await f.Service.EditManuallyAsync(new ConsumableId("C1"), VarietyA, ConsumableCondition.Used, new InStocker(Device, new SlotRef(1, 1)), None));
        f.Service.ReserveForRecovery([new ConsumableId("C1")]);
        Assert.Equal(ConfigurationChangeResult.InUse, await f.Service.DeleteStationAsync(Station, None));
        f.Service.ReleaseReservations([new ConsumableId("C1")]);
        Assert.Equal(ConfigurationChangeResult.Saved, await f.Service.DeleteStationAsync(Station, None));
    }

    [Fact]
    public async Task 登録していないステーションは変えるのも消すのも断る()
    {
        var f = Setup();

        Assert.Equal(ConfigurationChangeResult.NotFound, await f.Service.UpdateStationAsync(new StationLayout(new StationId("S9"), new PanelId("PS2")), Panels, None));
        Assert.Equal(ConfigurationChangeResult.NotFound, await f.Service.DeleteStationAsync(new StationId("S9"), None));
    }

    // ---- 読む ----

    [Fact]
    public async Task 構成を読むと装置とステーションの受け渡しパネルとふさがっているスロットと使われているかが出る()
    {
        var f = Setup();
        await f.Service.AddDeviceAsync(Layout("D2", "PD2", 1), Panels, None);
        await PutAsync(f, "C1", new InStocker(Device, new SlotRef(2, 1)));

        var configuration = f.Service.ReadConfiguration();

        Assert.Equal(["D1", "D2"], configuration.Devices.Select(d => d.Device.Value));
        var d1 = configuration.Devices[0];
        Assert.Equal(new PanelId("PD1"), d1.TransferPanel);
        Assert.Equal([2, 2], d1.SlotsPerStocker);
        Assert.Equal([new SlotRef(2, 1)], d1.OccupiedSlots);
        Assert.True(d1.InUse);
        Assert.False(configuration.Devices[1].InUse);
        Assert.Equal([new StationConfigurationView(Station, new PanelId("PS1"), InUse: false)], configuration.Stations);
    }
}
