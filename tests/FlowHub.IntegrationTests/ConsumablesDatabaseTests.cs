using FlowHub.Api.Database;
using FlowHub.Consumables;
using FlowHub.Consumables.Infrastructure;
using FlowHub.Dispatching;
using FlowHub.Map;

namespace FlowHub.IntegrationTests;

/// <summary>
/// 消耗品の管理の DB を、ローカルの PostgreSQL で確かめる（マイグレーションを当て、保存して読み直す）。
/// </summary>
public class ConsumablesDatabaseTests
{
    private static readonly DeviceId Device = new("D1");
    private static readonly ProcessTypeId Process = new("P");
    private static readonly VarietyId VarietyA = new("A");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task 構成とストッカーの設定と入庫の予定を保存し読み直せる()
    {
        var options = LocalPostgres.Options();
        await LocalPostgres.DropSchemaAsync(options, ConsumablesDatabase.Schema, Token);

        var store = await OpenAsync(options);
        await store.AddProcessTypeAsync(new ProcessTypeSettings(Process, new CarrierTypeId("X"), CarrierCapacity: 2), Token);
        await store.AddVarietyAsync(VarietyA, Process, Token);
        await store.AddDeviceAsync(new DeviceLayout(Device, new PanelId("D"), SlotsPerStocker: [2, 3]), Token);
        await store.AddStationAsync(new StationLayout(new StationId("S1"), new PanelId("S")), Token);
        store.SaveStockerSetting(Device, new Dictionary<VarietyId, int> { [VarietyA] = 2 });
        store.AddExpectedArrival(new ConsumableId("C-1"), VarietyA);

        var configuration = await (await OpenAsync(options)).LoadAsync(Token);

        Assert.Equal([2, 3], Assert.Single(configuration.Devices).SlotsPerStocker);
        Assert.Equal(new Dictionary<VarietyId, int> { [VarietyA] = 2 }, Assert.Single(configuration.StockerSettings).Counts);
        Assert.Equal([new ExpectedArrival(new ConsumableId("C-1"), VarietyA)], configuration.ExpectedArrivals);
    }

    private static Task<ConsumablesStore> OpenAsync(DatabaseOptions options) =>
        ConsumablesDatabase.OpenAsync(
            builder => Databases.Configure(builder, options, ConsumablesDatabase.Schema),
            (context, cancellationToken) => Databases.InitializeAsync(context, options, cancellationToken),
            Token);
}
