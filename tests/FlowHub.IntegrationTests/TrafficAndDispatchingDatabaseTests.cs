using FlowHub.Api.Database;
using FlowHub.Dispatching;
using FlowHub.Dispatching.Infrastructure;
using FlowHub.Routing;
using FlowHub.Traffic;
using FlowHub.Traffic.Infrastructure;

namespace FlowHub.IntegrationTests;

/// <summary>
/// 交通整理と配車の DB を、ローカルの PostgreSQL で確かめる（マイグレーションを当て、保存して読み直す）。
/// </summary>
public class TrafficAndDispatchingDatabaseTests
{
    private static readonly AgvId Agv = new("AGV-1");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task 交通整理のAGVの構成とコマンドの番号を保存し読み直せる()
    {
        var options = LocalPostgres.Options();
        await LocalPostgres.DropSchemaAsync(options, TrafficDatabase.Schema, Token);
        var motion = new MotionProfile(500, 250, 90);

        var store = await OpenTrafficAsync(options);
        await store.AddAgvTypeAsync("small", motion, Token);
        await store.AddAgvAsync(Agv, "small", Token);
        store.Save(Agv, 42);

        var reopened = await OpenTrafficAsync(options);

        Assert.Equal([new AgvConfiguration(Agv, motion)], await reopened.LoadAgvsAsync(Token));
        Assert.Equal(42, reopened.NextNumberOf(Agv));
    }

    [Fact]
    public async Task 交通整理のコントロールステータスを保存し読み直せる()
    {
        var options = LocalPostgres.Options();
        await LocalPostgres.DropSchemaAsync(options, TrafficDatabase.Schema, Token);

        var store = await OpenTrafficAsync(options);
        await store.AddAgvTypeAsync("small", new MotionProfile(500, 250, 90), Token);
        await store.AddAgvAsync(Agv, "small", Token);
        var registered = store.StatusOf(Agv);
        store.Save(Agv, ControlStatus.Locked);

        var reopened = await OpenTrafficAsync(options);

        Assert.Equal(ControlStatus.Detached, registered);
        Assert.Equal(ControlStatus.Locked, reopened.StatusOf(Agv));
    }

    [Fact]
    public async Task 配車のAGVの構成と充電の設定値を保存し読み直せる()
    {
        var options = LocalPostgres.Options();
        await LocalPostgres.DropSchemaAsync(options, DispatchingDatabase.Schema, Token);

        var store = await OpenDispatchingAsync(options);
        await store.SetChargingDefaultsAsync(25, 85, Token);
        await store.AddAgvTypeAsync("small", goChargeBelowPercent: 30, resumeAtPercent: null, Token);
        await store.AddAgvAsync(Agv, "small", new CarrierTypeId("X"), Token);

        var reopened = await OpenDispatchingAsync(options);

        Assert.Equal([new DispatchingAgvConfiguration(Agv, new CarrierTypeId("X"), 30, 85)], await reopened.LoadAgvsAsync(Token));
    }

    private static Task<TrafficStore> OpenTrafficAsync(DatabaseOptions options) =>
        TrafficDatabase.OpenAsync(
            builder => Databases.Configure(builder, options, TrafficDatabase.Schema),
            (context, cancellationToken) => Databases.InitializeAsync(context, options, cancellationToken),
            Token);

    private static Task<DispatchingStore> OpenDispatchingAsync(DatabaseOptions options) =>
        DispatchingDatabase.OpenAsync(
            builder => Databases.Configure(builder, options, DispatchingDatabase.Schema),
            (context, cancellationToken) => Databases.InitializeAsync(context, options, cancellationToken),
            Token);
}
