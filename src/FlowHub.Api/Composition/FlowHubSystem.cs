using FlowHub.Api.Composition.Screens;
using FlowHub.Api.Database;
using FlowHub.Api.InitialData;
using FlowHub.BuildingBlocks;
using FlowHub.Consumables;
using FlowHub.Consumables.Application;
using FlowHub.Consumables.Infrastructure;
using FlowHub.Dispatching.Application;
using FlowHub.Dispatching.Infrastructure;
using FlowHub.Map.Infrastructure;
using FlowHub.Simulator;
using FlowHub.ToolPlanning.Application;
using FlowHub.Traffic;
using FlowHub.Traffic.Application;
using FlowHub.Traffic.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlowHub.Api.Composition;

/// <summary>
/// 各コンテキストの組み立て。起動するときに DB を開いて構成を読み、サービスを作って、購読をつなぐ。
/// </summary>
/// <remarks>
/// 起動したときは、運転状態を停止から始める（docs/domain.md「システムの運転状態」）。
/// イベントは、処理（HTTP のリクエスト、シミュレーターの通知、交通整理のタイマー）を終えたあとにまとめて届け、
/// そのあとで画面へ様子を送る（<see cref="AfterWorkAsync"/>。後始末はここにまとめる）。
/// 装置・ステーション・上位システムとのやり取りは、本物の通信ができるまでシミュレーターで仮に実装する。
/// </remarks>
internal sealed partial class FlowHubSystem : IDisposable
{
    private readonly AgvEndpointOptions _agvEndpoints;
    private readonly TimeProvider _timeProvider;
    private volatile ScreenFeed? _screen;

    private FlowHubSystem(
        InProcessEventBus events,
        TrafficService traffic,
        DispatchingService dispatching,
        ConsumablesService consumables,
        ToolPlanningService toolPlanning,
        AgvEndpointOptions agvEndpoints,
        TimeProvider timeProvider)
    {
        Events = events;
        Traffic = traffic;
        Dispatching = dispatching;
        Consumables = consumables;
        ToolPlanning = toolPlanning;
        _agvEndpoints = agvEndpoints;
        _timeProvider = timeProvider;
    }

    public InProcessEventBus Events { get; }

    public TrafficService Traffic { get; }

    public DispatchingService Dispatching { get; }

    public ConsumablesService Consumables { get; }

    public ToolPlanningService ToolPlanning { get; }

    /// <summary>
    /// DB を開いて構成を読み、各コンテキストを組み立てる。
    /// </summary>
    public static async Task<FlowHubSystem> StartAsync(
        FlowHubSettings settings, TimeProvider timeProvider, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var database = settings.Database;
        void Configure(DbContextOptionsBuilder builder, string schema) => Databases.Configure(builder, database, schema);
        Task Initialize(DbContext context, CancellationToken token) => Databases.InitializeAsync(context, database, token);

        var map = await MapDatabase.OpenAsync(b => Configure(b, MapDatabase.Schema), Initialize, cancellationToken).ConfigureAwait(false);
        var trafficStore = await TrafficDatabase.OpenAsync(b => Configure(b, TrafficDatabase.Schema), Initialize, cancellationToken).ConfigureAwait(false);
        var dispatchingStore = await DispatchingDatabase.OpenAsync(b => Configure(b, DispatchingDatabase.Schema), Initialize, cancellationToken).ConfigureAwait(false);
        var consumablesStore = await ConsumablesDatabase.OpenAsync(b => Configure(b, ConsumablesDatabase.Schema), Initialize, cancellationToken).ConfigureAwait(false);
        var logger = loggerFactory.CreateLogger<FlowHubSystem>();
        if (settings.InitialDataPath is { } initialData)
        {
            await InitialDataSeeder.SeedIfEmptyAsync(initialData, map, trafficStore, dispatchingStore, consumablesStore, logger, cancellationToken).ConfigureAwait(false);
        }

        var subscriptions = new EventSubscriptions();
        var events = new InProcessEventBus(subscriptions);
        var simulator = new EquipmentSimulator(settings.Simulator, timeProvider, loggerFactory.CreateLogger<EquipmentSimulator>());

        var traffic = new TrafficService(map, events, timeProvider, trafficStore, settings.Planning, controlStatuses: trafficStore);
        var dispatching = new DispatchingService(traffic, map, events, timeProvider);
        await RegisterAgvsAsync(traffic, trafficStore, dispatching, dispatchingStore, logger, cancellationToken).ConfigureAwait(false);

        var consumables = new ConsumablesService(dispatching, simulator, simulator, simulator, events, timeProvider, consumablesStore);
        consumables.LoadConfiguration(await consumablesStore.LoadAsync(cancellationToken).ConfigureAwait(false));
        var toolPlanning = new ToolPlanningService(consumables, dispatching, timeProvider);

        DispatchingSubscriptions.Register(subscriptions, dispatching);
        ConsumablesSubscriptions.Register(subscriptions, consumables);
        ToolPlanningSubscriptions.Register(subscriptions, toolPlanning);
        ApiSubscriptions.Register(subscriptions, loggerFactory);

        var system = new FlowHubSystem(events, traffic, dispatching, consumables, toolPlanning, settings.AgvEndpoints, timeProvider);

        // シミュレーターの通知も、処理を終えたあとに後始末をする
        simulator.Attach(new ConsumablesSimulationTarget(consumables), system.AfterWorkAsync);

        // 交通整理のタイマー（緊急停止が長引いたときの立て直し）のあとも、後始末をする
        traffic.AttachAfterBackgroundWork(system.AfterWorkAsync);

        // 起動したときは、いつも停止から始める（設定があれば、すぐスタートにする）
        await dispatching.StopAsync(cancellationToken).ConfigureAwait(false);
        if (settings.StartOnLaunch)
        {
            await dispatching.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        await system.AfterWorkAsync(cancellationToken).ConfigureAwait(false);
        return system;
    }

    /// <summary>
    /// 画面とつなぐ SignalR のサービスを登録する（<c>app.Build()</c> の前に呼ぶ）。
    /// </summary>
    public static void AddScreenServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSignalR();
        services.AddSingleton<IScreenBroadcast, HubScreenBroadcast>();
        services.AddSingleton(provider => new ScreenFeed(
            provider.GetRequiredService<IScreenBroadcast>(),
            TimeProvider.System,
            provider.GetRequiredService<ILogger<ScreenFeed>>()));
    }

    /// <summary>
    /// 画面へ送る AGV の様子を読む。交通整理の様子と、配車の割り当て済みの搬送を合わせる（それぞれの lock で別々に読む）。
    /// </summary>
    private IReadOnlyList<AgvSnapshot> ReadAgvs()
    {
        var assignments = Dispatching.ListAssignments().ToDictionary(a => a.Agv);
        return [.. Traffic.ListAgvs().Select(view => AgvSnapshot.From(view, assignments.GetValueOrDefault(view.Id), TransferEndpointNames.DisplayName))];
    }

    /// <summary>
    /// 処理のあとの後始末。たまったイベントを届けてから、画面へ様子を送る（変わっていれば）。
    /// HTTP のリクエスト、シミュレーターの通知、交通整理のタイマーのあとは、すべてここを通す。
    /// </summary>
    public async Task AfterWorkAsync(CancellationToken cancellationToken)
    {
        await Events.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (_screen is { } screen)
        {
            await screen.PublishIfChangedAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// HTTP の窓口と、画面とつなぐハブ（<c>/hubs/screen</c>）を開く。リクエストを処理し終えたら、後始末をする。
    /// </summary>
    /// <remarks>先に <see cref="AddScreenServices"/> でサービスを登録しておく。</remarks>
    public void MapEndpoints(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var screen = app.Services.GetRequiredService<ScreenFeed>();
        screen.Attach(new ScreenSources(() => Dispatching.IsRunning, ReadAgvs));
        _screen = screen;

        app.Use(async (context, next) =>
        {
            await next(context).ConfigureAwait(false);

            // リクエストが途中で切れても、受け付けた処理のイベントは届け、画面へ様子を送る
            await AfterWorkAsync(CancellationToken.None).ConfigureAwait(false);
        });
        app.MapHub<ScreenHub>("/hubs/screen");
        app.MapAgvEndpoints(Traffic, _agvEndpoints, _timeProvider);
        app.MapSystemEndpoints(Dispatching);
        app.MapTransportEndpoints(Dispatching);
        app.MapAgvOperationEndpoints(Traffic, Dispatching, app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FlowHub.Api.AgvOperations"));
    }

    public void Dispose()
    {
        ToolPlanning.Dispose();
        Consumables.Dispose();
        Dispatching.Dispose();
        Traffic.Dispose();
    }

    /// <summary>
    /// 交通整理と配車の両方に登録されている AGV だけを登録する。片方にしかない AGV は、ログで警告する（画面での通知は backlog #10）。
    /// </summary>
    private static async Task RegisterAgvsAsync(
        TrafficService traffic,
        TrafficStore trafficStore,
        DispatchingService dispatching,
        DispatchingStore dispatchingStore,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var inTraffic = await trafficStore.LoadAgvsAsync(cancellationToken).ConfigureAwait(false);
        var inDispatching = (await dispatchingStore.LoadAgvsAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(a => a.Id);

        foreach (var agv in inTraffic)
        {
            if (!inDispatching.Remove(agv.Id, out var dispatchingAgv))
            {
                LogMissingIn(logger, agv.Id.ToString(), "配車");
                continue;
            }

            traffic.RegisterAgv(agv.Id, agv.Motion, agv.CanTravelSpecialTransfer);
            dispatching.RegisterAgv(agv.Id, dispatchingAgv.Carrier, dispatchingAgv.GoChargeBelowPercent, dispatchingAgv.ResumeAtPercent);
        }

        foreach (var id in inDispatching.Keys.OrderBy(id => id.Value, StringComparer.Ordinal))
        {
            LogMissingIn(logger, id.ToString(), "交通整理");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Agv} は {Missing} に登録されていないので、登録しませんでした。交通整理と配車の両方に登録してください。")]
    private static partial void LogMissingIn(ILogger logger, string agv, string missing);
}
