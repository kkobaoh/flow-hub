using FlowHub.BuildingBlocks;
using FlowHub.Consumables.Application;
using FlowHub.Dispatching;
using FlowHub.Map;

namespace FlowHub.Consumables.Tests;

/// <summary>
/// 消耗品の構成（加工の種類、品種、入庫の予定）を、動いている間に加える・変える・消すテスト。
/// </summary>
public class ConsumablesConfigurationTests
{
    private static readonly DeviceId Device = new("D1");
    private static readonly StationId Station = new("S1");
    private static readonly ProcessTypeId Process = new("P");
    private static readonly ProcessTypeId OtherProcess = new("Q");
    private static readonly VarietyId VarietyA = new("A");
    private static readonly VarietyId VarietyB = new("B");
    private static readonly CancellationToken None = CancellationToken.None;

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

    private sealed class RecordingUpstream : IUpstreamGateway
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

    /// <summary>保存を頼まれたものを、文で残す。</summary>
    private sealed class RecordingPersistence : IConsumablesPersistence
    {
        public List<string> Calls { get; } = [];

        public void SaveStockerSetting(DeviceId device, IReadOnlyDictionary<VarietyId, int> counts) =>
            Calls.Add($"setting {device} {string.Join(",", counts.OrderBy(p => p.Key.Value, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}"))}");

        public void AddExpectedArrival(ConsumableId consumable, VarietyId variety) => Calls.Add($"arrival+ {consumable} {variety}");

        public void RemoveExpectedArrival(ConsumableId consumable) => Calls.Add($"arrival- {consumable}");

        public void SaveProcessType(ProcessTypeSettings settings) => Calls.Add($"process {settings.Id} {settings.Carrier} {settings.CarrierCapacity}");

        public void DeleteProcessType(ProcessTypeId processType) => Calls.Add($"process- {processType}");

        public void SaveVariety(VarietyId variety, ProcessTypeId processType) => Calls.Add($"variety {variety} {processType}");

        public void DeleteVariety(VarietyId variety) => Calls.Add($"variety- {variety}");
    }

    private sealed record Fixture(ConsumablesService Service, RecordingPersistence Saved, RecordingPublisher Events, RecordingUpstream Upstream);

    /// <summary>装置 D1（スロット4つ）、ステーション S1、加工の種類 P（キャリア X、2個）と Q（キャリア Y、3個）。品種 A、B は P。</summary>
    private static Fixture Setup()
    {
        var saved = new RecordingPersistence();
        var events = new RecordingPublisher();
        var upstream = new RecordingUpstream();
        var service = new ConsumablesService(new NoDispatching(), new NoDevices(), new NoStations(), upstream, events, TimeProvider.System, saved);
        service.RegisterDevice(new DeviceLayout(Device, new PanelId("D"), SlotsPerStocker: [4]));
        service.RegisterStation(new StationLayout(Station, new PanelId("S")));
        service.RegisterProcessType(new ProcessTypeSettings(Process, new CarrierTypeId("X"), CarrierCapacity: 2));
        service.RegisterProcessType(new ProcessTypeSettings(OtherProcess, new CarrierTypeId("Y"), CarrierCapacity: 3));
        service.RegisterVariety(VarietyA, Process);
        service.RegisterVariety(VarietyB, Process);
        return new Fixture(service, saved, events, upstream);
    }

    private static Task<ManualEntryResult> StockAsync(Fixture f, string id, VarietyId variety) =>
        f.Service.RegisterManuallyAsync(new ConsumableId(id), variety, ConsumableCondition.New, new AtStation(Station), None);

    // ---- 加工の種類 ----

    [Fact]
    public async Task 加工の種類を加えると保存してキャリアの種類の候補にも入る()
    {
        var f = Setup();

        var result = await f.Service.AddProcessTypeAsync(new ProcessTypeSettings(new ProcessTypeId("R"), new CarrierTypeId("Z"), 4), None);

        Assert.Equal(ConfigurationChangeResult.Saved, result);
        Assert.Contains("process R Z 4", f.Saved.Calls);
        Assert.Contains(new CarrierTypeId("Z"), f.Service.CarrierTypes);
        Assert.Contains(f.Service.ReadConfiguration().ProcessTypes, p => p.Id == new ProcessTypeId("R"));
        Assert.Single(f.Events.Published.OfType<StockChanged>());
    }

    [Fact]
    public async Task 同じ名前の加工の種類は加えるのを断る()
    {
        var f = Setup();

        var result = await f.Service.AddProcessTypeAsync(new ProcessTypeSettings(Process, new CarrierTypeId("Z"), 4), None);

        Assert.Equal(ConfigurationChangeResult.AlreadyRegistered, result);
        Assert.Equal(new CarrierTypeId("X"), f.Service.ProcessTypeOf(VarietyA).Carrier);
        Assert.Empty(f.Saved.Calls);
        Assert.Empty(f.Events.Published);
    }

    [Fact]
    public async Task 加工の種類のキャリアの種類と個数は品種があっても変えられ次の判断から効く()
    {
        var f = Setup();
        Assert.Equal(ManualEntryResult.Saved, await StockAsync(f, "C1", VarietyA));

        var result = await f.Service.UpdateProcessTypeAsync(new ProcessTypeSettings(Process, new CarrierTypeId("Z"), 5), None);

        Assert.Equal(ConfigurationChangeResult.Saved, result);
        var settings = f.Service.ProcessTypeOf(VarietyA);
        Assert.Equal(new CarrierTypeId("Z"), settings.Carrier);
        Assert.Equal(5, settings.CarrierCapacity);
        Assert.Contains("process P Z 5", f.Saved.Calls);
        Assert.DoesNotContain(new CarrierTypeId("X"), f.Service.CarrierTypes);
    }

    [Fact]
    public async Task 登録していない加工の種類は変えるのも消すのも断る()
    {
        var f = Setup();
        var unknown = new ProcessTypeId("R");

        Assert.Equal(ConfigurationChangeResult.NotFound, await f.Service.UpdateProcessTypeAsync(new ProcessTypeSettings(unknown, new CarrierTypeId("Z"), 1), None));
        Assert.Equal(ConfigurationChangeResult.NotFound, await f.Service.DeleteProcessTypeAsync(unknown, None));
        Assert.Empty(f.Saved.Calls);
    }

    [Fact]
    public async Task 品種のない加工の種類は消せる()
    {
        var f = Setup();

        var result = await f.Service.DeleteProcessTypeAsync(OtherProcess, None);

        Assert.Equal(ConfigurationChangeResult.Saved, result);
        Assert.Contains("process- Q", f.Saved.Calls);
        Assert.DoesNotContain(f.Service.ReadConfiguration().ProcessTypes, p => p.Id == OtherProcess);
        Assert.DoesNotContain(new CarrierTypeId("Y"), f.Service.CarrierTypes);
    }

    [Fact]
    public async Task 品種のある加工の種類は消すのを断る()
    {
        var f = Setup();

        var result = await f.Service.DeleteProcessTypeAsync(Process, None);

        Assert.Equal(ConfigurationChangeResult.InUse, result);
        Assert.Contains(f.Service.ReadConfiguration().ProcessTypes, p => p.Id == Process);
        Assert.Empty(f.Saved.Calls);
    }

    // ---- 品種 ----

    [Fact]
    public async Task 品種を加えると保存する()
    {
        var f = Setup();
        var variety = new VarietyId("C");

        var result = await f.Service.AddVarietyAsync(variety, OtherProcess, None);

        Assert.Equal(ConfigurationChangeResult.Saved, result);
        Assert.Contains("variety C Q", f.Saved.Calls);
        Assert.Equal(OtherProcess, f.Service.ProcessTypeOf(variety).Id);
        Assert.Single(f.Events.Published.OfType<StockChanged>());
    }

    [Fact]
    public async Task 同じ名前の品種と登録していない加工の種類の品種は加えるのを断る()
    {
        var f = Setup();

        Assert.Equal(ConfigurationChangeResult.AlreadyRegistered, await f.Service.AddVarietyAsync(VarietyA, OtherProcess, None));
        Assert.Equal(ConfigurationChangeResult.NotFound, await f.Service.AddVarietyAsync(new VarietyId("C"), new ProcessTypeId("R"), None));
        Assert.Empty(f.Saved.Calls);
        Assert.Equal(Process, f.Service.ProcessTypeOf(VarietyA).Id);
    }

    [Fact]
    public async Task 使われていない品種は加工の種類を変えられる()
    {
        var f = Setup();

        var result = await f.Service.UpdateVarietyAsync(VarietyB, OtherProcess, None);

        Assert.Equal(ConfigurationChangeResult.Saved, result);
        Assert.Equal(OtherProcess, f.Service.ProcessTypeOf(VarietyB).Id);
        Assert.Contains("variety B Q", f.Saved.Calls);
    }

    [Fact]
    public async Task 消耗品の記録にある品種は加工の種類を変えるのも消すのも断る()
    {
        var f = Setup();
        Assert.Equal(ManualEntryResult.Saved, await StockAsync(f, "C1", VarietyA));
        f.Saved.Calls.Clear();

        Assert.Equal(ConfigurationChangeResult.InUse, await f.Service.UpdateVarietyAsync(VarietyA, OtherProcess, None));
        Assert.Equal(ConfigurationChangeResult.InUse, await f.Service.DeleteVarietyAsync(VarietyA, None));
        Assert.Equal(Process, f.Service.ProcessTypeOf(VarietyA).Id);
        Assert.Empty(f.Saved.Calls);
    }

    [Fact]
    public async Task ストッカーの設定で1個以上にしている品種は加工の種類を変えるのも消すのも断る()
    {
        var f = Setup();
        await f.Service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 1 }, None);
        f.Saved.Calls.Clear();

        Assert.Equal(ConfigurationChangeResult.InUse, await f.Service.UpdateVarietyAsync(VarietyA, OtherProcess, None));
        Assert.Equal(ConfigurationChangeResult.InUse, await f.Service.DeleteVarietyAsync(VarietyA, None));
        Assert.Empty(f.Saved.Calls);
    }

    [Fact]
    public async Task ストッカーの設定で0個の品種は使っていない扱いで消すと設定の0の行も消える()
    {
        var f = Setup();
        await f.Service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 0, [VarietyB] = 2 }, None);
        f.Saved.Calls.Clear();

        var result = await f.Service.DeleteVarietyAsync(VarietyA, None);

        Assert.Equal(ConfigurationChangeResult.Saved, result);
        Assert.Equal(["variety- A"], f.Saved.Calls);
        var configuration = f.Service.ReadConfiguration();
        Assert.DoesNotContain(configuration.Varieties, v => v.Variety == VarietyA);
        Assert.Equal([new StockerLine(VarietyB, 2, 0)], f.Service.ReadStatuses().Devices.Single().Stocker);
    }

    [Fact]
    public async Task 品種を消すと入庫の予定も一緒に消えそのIDは未登録として入庫する()
    {
        var f = Setup();
        f.Service.RegisterExpectedArrival(new ConsumableId("C9"), VarietyB);
        f.Saved.Calls.Clear();

        Assert.Equal(ConfigurationChangeResult.Saved, await f.Service.DeleteVarietyAsync(VarietyB, None));
        Assert.Equal(["variety- B"], f.Saved.Calls);

        await f.Service.ReportReadAtStationAsync(Station, new ConsumableId("C9"), None);
        var view = f.Service.GetConsumable(new ConsumableId("C9"));
        Assert.Null(view.Variety);
        Assert.True(view.NeedsCheck);
    }

    [Fact]
    public async Task 品種を消すと補充を頼んだ数も忘れる()
    {
        var f = Setup();
        await f.Service.RequestReplenishmentAsync(VarietyB, 2, None);

        Assert.Equal(ConfigurationChangeResult.Saved, await f.Service.DeleteVarietyAsync(VarietyB, None));
        Assert.Equal(ConfigurationChangeResult.Saved, await f.Service.AddVarietyAsync(VarietyB, Process, None));
        await f.Service.RequestReplenishmentAsync(VarietyB, 2, None);

        Assert.Equal([(VarietyB, 2), (VarietyB, 2)], f.Upstream.Requests);
    }

    [Fact]
    public async Task 登録していない品種は変えるのも消すのも断る()
    {
        var f = Setup();
        var unknown = new VarietyId("C");

        Assert.Equal(ConfigurationChangeResult.NotFound, await f.Service.UpdateVarietyAsync(unknown, Process, None));
        Assert.Equal(ConfigurationChangeResult.NotFound, await f.Service.UpdateVarietyAsync(VarietyB, new ProcessTypeId("R"), None));
        Assert.Equal(ConfigurationChangeResult.NotFound, await f.Service.DeleteVarietyAsync(unknown, None));
        Assert.Empty(f.Saved.Calls);
    }

    // ---- 入庫の予定 ----

    [Fact]
    public async Task 入庫の予定を加えて外せる()
    {
        var f = Setup();
        var id = new ConsumableId("C5");

        Assert.Equal(ConfigurationChangeResult.Saved, await f.Service.AddExpectedArrivalAsync(id, VarietyA, None));
        Assert.Equal([id], f.Service.ReadConfiguration().Varieties.Single(v => v.Variety == VarietyA).ExpectedIds);

        Assert.Equal(ConfigurationChangeResult.Saved, await f.Service.RemoveExpectedArrivalAsync(id, None));
        Assert.Empty(f.Service.ReadConfiguration().Varieties.Single(v => v.Variety == VarietyA).ExpectedIds);
        Assert.Equal(["arrival+ C5 A", "arrival- C5"], f.Saved.Calls);
        Assert.Equal(2, f.Events.Published.OfType<StockChanged>().Count());
    }

    [Fact]
    public async Task 入庫の予定にあるIDと消耗品の記録にあるIDは加えるのを断る()
    {
        var f = Setup();
        Assert.Equal(ConfigurationChangeResult.Saved, await f.Service.AddExpectedArrivalAsync(new ConsumableId("C5"), VarietyA, None));
        Assert.Equal(ManualEntryResult.Saved, await StockAsync(f, "C1", VarietyA));
        f.Saved.Calls.Clear();

        Assert.Equal(ConfigurationChangeResult.AlreadyRegistered, await f.Service.AddExpectedArrivalAsync(new ConsumableId("C5"), VarietyB, None));
        Assert.Equal(ConfigurationChangeResult.AlreadyRegistered, await f.Service.AddExpectedArrivalAsync(new ConsumableId("C1"), VarietyB, None));
        Assert.Equal(ConfigurationChangeResult.NotFound, await f.Service.AddExpectedArrivalAsync(new ConsumableId("C6"), new VarietyId("C"), None));
        Assert.Empty(f.Saved.Calls);
    }

    [Fact]
    public async Task 入庫の予定にないIDは外すのを断る()
    {
        var f = Setup();
        Assert.Equal(ManualEntryResult.Saved, await StockAsync(f, "C1", VarietyA));
        f.Saved.Calls.Clear();

        Assert.Equal(ConfigurationChangeResult.NotFound, await f.Service.RemoveExpectedArrivalAsync(new ConsumableId("C1"), None));
        Assert.Equal(ConfigurationChangeResult.NotFound, await f.Service.RemoveExpectedArrivalAsync(new ConsumableId("C2"), None));
        Assert.Empty(f.Saved.Calls);
    }

    // ---- 読む ----

    [Fact]
    public async Task 構成を読むと加工の種類と品種ごとの入庫の予定記録にあるID使われているかがIDの順に出る()
    {
        var f = Setup();
        await f.Service.AddExpectedArrivalAsync(new ConsumableId("C7"), VarietyA, None);
        await f.Service.AddExpectedArrivalAsync(new ConsumableId("C5"), VarietyA, None);
        await StockAsync(f, "C2", VarietyA);
        await StockAsync(f, "C1", VarietyA);

        var configuration = f.Service.ReadConfiguration();

        Assert.Equal([Process, OtherProcess], configuration.ProcessTypes.Select(p => p.Id));
        Assert.Equal([VarietyA, VarietyB], configuration.Varieties.Select(v => v.Variety));
        var a = configuration.Varieties[0];
        Assert.Equal(Process, a.ProcessType);
        Assert.Equal([new ConsumableId("C5"), new ConsumableId("C7")], a.ExpectedIds);
        Assert.Equal([new ConsumableId("C1"), new ConsumableId("C2")], a.StockedIds);
        Assert.True(a.InUse);
        Assert.False(configuration.Varieties[1].InUse);
    }
}
