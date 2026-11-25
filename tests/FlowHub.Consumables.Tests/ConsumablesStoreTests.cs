using FlowHub.Consumables.Application;
using FlowHub.Consumables.Infrastructure;
using FlowHub.Dispatching;
using FlowHub.Map;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FlowHub.Consumables.Tests;

/// <summary>
/// 消耗品の管理の DB（装置、ステーション、加工の種類、品種、ストッカーの設定、入庫の予定）のテスト。メモリ上の SQLite を使う。
/// </summary>
public sealed class ConsumablesStoreTests : IDisposable
{
    private static readonly DeviceId Device = new("D1");
    private static readonly StationId Station = new("S1");
    private static readonly ProcessTypeId Process = new("P");
    private static readonly VarietyId VarietyA = new("A");
    private static readonly VarietyId VarietyB = new("B");

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public ConsumablesStoreTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

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

    /// <summary>同じ接続を使うので、開き直すと「次に起動したとき」と同じになる。</summary>
    private Task<ConsumablesStore> OpenAsync() =>
        ConsumablesDatabase.OpenAsync(
            builder => builder.UseSqlite(_connection),
            (context, cancellationToken) => context.Database.EnsureCreatedAsync(cancellationToken),
            Token);

    /// <summary>装置 D1（ストッカー2つ、スロット 2 と 3）、ステーション S1、加工の種類 P、品種 A と B を登録する。</summary>
    private static async Task AddBasicsAsync(ConsumablesStore store)
    {
        await store.AddProcessTypeAsync(new ProcessTypeSettings(Process, new CarrierTypeId("X"), CarrierCapacity: 2), Token);
        await store.AddVarietyAsync(VarietyA, Process, Token);
        await store.AddVarietyAsync(VarietyB, Process, Token);
        await store.AddDeviceAsync(new DeviceLayout(Device, new PanelId("D"), SlotsPerStocker: [2, 3]), Token);
        await store.AddStationAsync(new StationLayout(Station, new PanelId("S")), Token);
    }

    /// <summary>保存してある構成を読み込んだ、消耗品の管理。</summary>
    private static ConsumablesService ServiceFrom(ConsumablesConfiguration configuration, ConsumablesStore store)
    {
        var service = new ConsumablesService(new NoDispatching(), new NoDevices(), new NoStations(), new IgnoringReplenishment(), new IgnoringPublisher(), new FixedTimeProvider(), store);
        service.LoadConfiguration(configuration);
        return service;
    }

    // ---- 構成のデータ ----

    [Fact]
    public async Task 登録した構成を次に開いたときに読む()
    {
        var store = await OpenAsync();
        await AddBasicsAsync(store);

        var configuration = await (await OpenAsync()).LoadAsync(Token);

        var device = Assert.Single(configuration.Devices);
        Assert.Equal((Device, new PanelId("D")), (device.Id, device.TransferPanel));
        Assert.Equal([2, 3], device.SlotsPerStocker);
        Assert.Equal([new StationLayout(Station, new PanelId("S"))], configuration.Stations);
        var processType = Assert.Single(configuration.ProcessTypes);
        Assert.Equal((Process, new CarrierTypeId("X"), 2), (processType.Id, processType.Carrier, processType.CarrierCapacity));
        Assert.Equal([(VarietyA, Process), (VarietyB, Process)], configuration.Varieties.Select(v => (v.Variety, v.ProcessType)));
        Assert.Empty(configuration.StockerSettings);
        Assert.Empty(configuration.ExpectedArrivals);
    }

    [Fact]
    public async Task 何も登録していなければ空()
    {
        var store = await OpenAsync();

        Assert.True(await store.IsEmptyAsync(Token));
    }

    [Fact]
    public async Task 登録されていない加工の種類の品種は登録できない()
    {
        var store = await OpenAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddVarietyAsync(VarietyA, new ProcessTypeId("Q"), Token));
    }

    // ---- ストッカーの設定 ----

    [Fact]
    public async Task ストッカーの設定を変えたら保存し次に開いたときも同じ設定で判断する()
    {
        var store = await OpenAsync();
        await AddBasicsAsync(store);
        using (var service = ServiceFrom(await store.LoadAsync(Token), store))
        {
            await service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 2, [VarietyB] = 0 }, Token);
        }

        var reopened = await OpenAsync();
        using var restarted = ServiceFrom(await reopened.LoadAsync(Token), reopened);

        var setting = Assert.Single((await reopened.LoadAsync(Token)).StockerSettings);
        Assert.Equal(Device, setting.Device);
        Assert.Equal(new Dictionary<VarietyId, int> { [VarietyA] = 2, [VarietyB] = 0 }, setting.Counts);
        Assert.Equal(new Dictionary<VarietyId, int> { [VarietyA] = 2 }, restarted.EvaluateDevice(Device).Shortages);
    }

    [Fact]
    public async Task ストッカーの設定を変え直したら置き換わる()
    {
        var store = await OpenAsync();
        await AddBasicsAsync(store);
        using var service = ServiceFrom(await store.LoadAsync(Token), store);
        await service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 2 }, Token);

        await service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyB] = 1 }, Token);

        var setting = Assert.Single((await (await OpenAsync()).LoadAsync(Token)).StockerSettings);
        Assert.Equal(new Dictionary<VarietyId, int> { [VarietyB] = 1 }, setting.Counts);
    }

    [Fact]
    public async Task 空の設定も設定ありとして残る()
    {
        // 空の設定は、まだ設定していないのとは違う（ストッカーにある新品は、すべて設定にない品種になる）
        var store = await OpenAsync();
        await AddBasicsAsync(store);
        using (var service = ServiceFrom(await store.LoadAsync(Token), store))
        {
            await service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int>(), Token);
        }

        var setting = Assert.Single((await (await OpenAsync()).LoadAsync(Token)).StockerSettings);

        Assert.Equal(Device, setting.Device);
        Assert.Empty(setting.Counts);
    }

    // ---- 入庫の予定 ----

    [Fact]
    public async Task 入庫の予定を保存し次に開いたときもその品種で入庫できる()
    {
        var store = await OpenAsync();
        await AddBasicsAsync(store);
        using (var service = ServiceFrom(await store.LoadAsync(Token), store))
        {
            service.RegisterExpectedArrival(new ConsumableId("C-1"), VarietyA);
        }

        var reopened = await OpenAsync();
        using var restarted = ServiceFrom(await reopened.LoadAsync(Token), reopened);
        await restarted.ReportReadAtStationAsync(Station, new ConsumableId("C-1"), Token);

        Assert.Equal(VarietyA, restarted.GetConsumable(new ConsumableId("C-1")).Variety);
    }

    [Fact]
    public async Task 入庫したら入庫の予定を消す()
    {
        var store = await OpenAsync();
        await AddBasicsAsync(store);
        using var service = ServiceFrom(await store.LoadAsync(Token), store);
        service.RegisterExpectedArrival(new ConsumableId("C-1"), VarietyA);

        await service.ReportReadAtStationAsync(Station, new ConsumableId("C-1"), Token);

        Assert.Empty((await (await OpenAsync()).LoadAsync(Token)).ExpectedArrivals);
    }

    [Fact]
    public async Task 読み込んだ構成は保存し直さない()
    {
        // 入庫の予定を読み込むときに、もう一度保存しようとすると二重になって失敗する
        var store = await OpenAsync();
        await AddBasicsAsync(store);
        using (var service = ServiceFrom(await store.LoadAsync(Token), store))
        {
            service.RegisterExpectedArrival(new ConsumableId("C-1"), VarietyA);
        }

        var reopened = await OpenAsync();
        using var restarted = ServiceFrom(await reopened.LoadAsync(Token), reopened);

        Assert.Equal([(new ConsumableId("C-1"), VarietyA)], (await reopened.LoadAsync(Token)).ExpectedArrivals.Select(e => (e.Consumable, e.Variety)));
    }

    [Fact]
    public void DBを使わないときは何も保存しない()
    {
        var persistence = NoConsumablesPersistence.Instance;

        persistence.SaveStockerSetting(Device, new Dictionary<VarietyId, int> { [VarietyA] = 1 });
        persistence.AddExpectedArrival(new ConsumableId("C-1"), VarietyA);
        persistence.RemoveExpectedArrival(new ConsumableId("C-1"));
        persistence.SaveProcessType(new ProcessTypeSettings(Process, new CarrierTypeId("X"), 1));
        persistence.SaveVariety(VarietyA, Process);
        persistence.DeleteVariety(VarietyA);
        persistence.DeleteProcessType(Process);
        persistence.SaveDevice(new DeviceLayout(Device, new PanelId("D"), [1]));
        persistence.DeleteDevice(Device);
        persistence.SaveStation(new StationLayout(Station, new PanelId("S")));
        persistence.DeleteStation(Station);
    }

    [Fact]
    public async Task 画面から変えた装置とステーションを保存し次に開いたときも同じ構成になる()
    {
        var store = await OpenAsync();
        await AddBasicsAsync(store);
        var panels = new HashSet<PanelId> { new("D"), new("S"), new("D2"), new("S2") };
        using (var service = ServiceFrom(await store.LoadAsync(Token), store))
        {
            await service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 1 }, Token);
            Assert.Equal(ConfigurationChangeResult.Saved, await service.AddDeviceAsync(new DeviceLayout(new DeviceId("D2"), new PanelId("D2"), [4]), panels, Token));
            Assert.Equal(ConfigurationChangeResult.Saved, await service.UpdateDeviceAsync(new DeviceLayout(new DeviceId("D2"), new PanelId("D2"), [1, 2]), panels, Token));
            Assert.Equal(ConfigurationChangeResult.Saved, await service.DeleteDeviceAsync(Device, Token));
            Assert.Equal(ConfigurationChangeResult.Saved, await service.UpdateStationAsync(new StationLayout(Station, new PanelId("S2")), panels, Token));
        }

        var configuration = await (await OpenAsync()).LoadAsync(Token);

        var device = Assert.Single(configuration.Devices);
        Assert.Equal((new DeviceId("D2"), new PanelId("D2")), (device.Id, device.TransferPanel));
        Assert.Equal([1, 2], device.SlotsPerStocker);
        Assert.Empty(configuration.StockerSettings); // 消した装置のストッカーの設定も消える
        Assert.Equal([new StationLayout(Station, new PanelId("S2"))], configuration.Stations);

        using (var service = ServiceFrom(configuration, await OpenAsync()))
        {
            Assert.Equal(ConfigurationChangeResult.Saved, await service.DeleteStationAsync(Station, Token));
        }

        Assert.Empty((await (await OpenAsync()).LoadAsync(Token)).Stations);
    }

    [Fact]
    public async Task 画面から変えた加工の種類と品種を保存し次に開いたときも同じ構成になる()
    {
        var store = await OpenAsync();
        await AddBasicsAsync(store);
        using (var service = ServiceFrom(await store.LoadAsync(Token), store))
        {
            var other = new ProcessTypeId("Q");
            Assert.Equal(ConfigurationChangeResult.Saved, await service.AddProcessTypeAsync(new ProcessTypeSettings(other, new CarrierTypeId("Y"), 3), Token));
            Assert.Equal(ConfigurationChangeResult.Saved, await service.UpdateProcessTypeAsync(new ProcessTypeSettings(Process, new CarrierTypeId("Z"), 4), Token));
            Assert.Equal(ConfigurationChangeResult.Saved, await service.AddVarietyAsync(new VarietyId("C"), other, Token));
            Assert.Equal(ConfigurationChangeResult.Saved, await service.UpdateVarietyAsync(VarietyB, other, Token));
        }

        var configuration = await (await OpenAsync()).LoadAsync(Token);

        Assert.Equal(
            [(Process, new CarrierTypeId("Z"), 4), (new ProcessTypeId("Q"), new CarrierTypeId("Y"), 3)],
            configuration.ProcessTypes.Select(p => (p.Id, p.Carrier, p.CarrierCapacity)));
        Assert.Equal(
            [(VarietyA, Process), (VarietyB, new ProcessTypeId("Q")), (new VarietyId("C"), new ProcessTypeId("Q"))],
            configuration.Varieties.Select(v => (v.Variety, v.ProcessType)));
    }

    [Fact]
    public async Task 品種を消すと入庫の予定とストッカーの設定の行も一緒に消え品種のなくなった加工の種類も消せる()
    {
        var store = await OpenAsync();
        await AddBasicsAsync(store);
        using (var service = ServiceFrom(await store.LoadAsync(Token), store))
        {
            await service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 0, [VarietyB] = 1 }, Token);
            Assert.Equal(ConfigurationChangeResult.Saved, await service.AddExpectedArrivalAsync(new ConsumableId("C-1"), VarietyA, Token));
            Assert.Equal(ConfigurationChangeResult.Saved, await service.AddExpectedArrivalAsync(new ConsumableId("C-2"), VarietyB, Token));

            Assert.Equal(ConfigurationChangeResult.Saved, await service.DeleteVarietyAsync(VarietyA, Token));
            await service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int>(), Token);
            Assert.Equal(ConfigurationChangeResult.Saved, await service.DeleteVarietyAsync(VarietyB, Token));
            Assert.Equal(ConfigurationChangeResult.Saved, await service.DeleteProcessTypeAsync(Process, Token));
        }

        var configuration = await (await OpenAsync()).LoadAsync(Token);

        Assert.Empty(configuration.ProcessTypes);
        Assert.Empty(configuration.Varieties);
        Assert.Empty(configuration.ExpectedArrivals);
        Assert.Empty(Assert.Single(configuration.StockerSettings).Counts);
    }

    [Fact]
    public async Task 品種を消してもほかの品種のストッカーの設定と入庫の予定は残る()
    {
        var store = await OpenAsync();
        await AddBasicsAsync(store);
        using (var service = ServiceFrom(await store.LoadAsync(Token), store))
        {
            await service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 0, [VarietyB] = 1 }, Token);
            await service.AddExpectedArrivalAsync(new ConsumableId("C-2"), VarietyB, Token);
            Assert.Equal(ConfigurationChangeResult.Saved, await service.DeleteVarietyAsync(VarietyA, Token));
        }

        var configuration = await (await OpenAsync()).LoadAsync(Token);

        Assert.Equal(new Dictionary<VarietyId, int> { [VarietyB] = 1 }, Assert.Single(configuration.StockerSettings).Counts);
        Assert.Equal([(new ConsumableId("C-2"), VarietyB)], configuration.ExpectedArrivals.Select(e => (e.Consumable, e.Variety)));
    }
}
