using FlowHub.Api.Composition.Screens;
using FlowHub.Api.Database;
using FlowHub.Api.InitialData;
using FlowHub.BuildingBlocks;
using FlowHub.Consumables;
using FlowHub.Consumables.Application;
using FlowHub.Consumables.Infrastructure;
using FlowHub.Dispatching.Application;
using FlowHub.Dispatching.Infrastructure;
using FlowHub.Map;
using FlowHub.Map.Infrastructure;
using FlowHub.Notifications;
using FlowHub.Notifications.Infrastructure;
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
    private readonly IMapStore _map;
    private volatile ScreenFeed? _screen;

    private FlowHubSystem(
        InProcessEventBus events,
        TrafficService traffic,
        DispatchingService dispatching,
        ConsumablesService consumables,
        ToolPlanningService toolPlanning,
        INoticeBoard notices,
        IMapStore map,
        AgvEndpointOptions agvEndpoints,
        TimeProvider timeProvider)
    {
        Events = events;
        Traffic = traffic;
        Dispatching = dispatching;
        Consumables = consumables;
        ToolPlanning = toolPlanning;
        Notices = notices;
        _map = map;
        _agvEndpoints = agvEndpoints;
        _timeProvider = timeProvider;
    }

    public InProcessEventBus Events { get; }

    public TrafficService Traffic { get; }

    public DispatchingService Dispatching { get; }

    public ConsumablesService Consumables { get; }

    public ToolPlanningService ToolPlanning { get; }

    /// <summary>画面で知らせるメッセージの置き場。</summary>
    public INoticeBoard Notices { get; }

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
        var notices = await NotificationsDatabase.OpenAsync(b => Configure(b, NotificationsDatabase.Schema), Initialize, timeProvider, settings.NoticeCapacity, cancellationToken).ConfigureAwait(false);
        var logger = loggerFactory.CreateLogger<FlowHubSystem>();
        if (settings.InitialDataPath is { } initialData)
        {
            await InitialDataSeeder.SeedIfEmptyAsync(initialData, map, trafficStore, dispatchingStore, consumablesStore, settings.MapValidation, logger, cancellationToken).ConfigureAwait(false);
        }

        var subscriptions = new EventSubscriptions();
        var events = new InProcessEventBus(subscriptions);
        var simulator = new EquipmentSimulator(settings.Simulator, timeProvider, loggerFactory.CreateLogger<EquipmentSimulator>());

        var traffic = new TrafficService(map, events, timeProvider, trafficStore, settings.Planning, controlStatuses: trafficStore);
        var dispatching = new DispatchingService(traffic, map, events, timeProvider);
        await RegisterAgvsAsync(traffic, trafficStore, dispatching, dispatchingStore, notices, logger, cancellationToken).ConfigureAwait(false);

        var consumables = new ConsumablesService(dispatching, simulator, simulator, simulator, events, timeProvider, consumablesStore);
        consumables.LoadConfiguration(await consumablesStore.LoadAsync(cancellationToken).ConfigureAwait(false));
        var toolPlanning = new ToolPlanningService(consumables, dispatching, timeProvider);

        DispatchingSubscriptions.Register(subscriptions, dispatching);
        ConsumablesSubscriptions.Register(subscriptions, consumables);
        ToolPlanningSubscriptions.Register(subscriptions, toolPlanning);
        ApiSubscriptions.Register(subscriptions, loggerFactory, notices);

        var system = new FlowHubSystem(events, traffic, dispatching, consumables, toolPlanning, notices, map, settings.AgvEndpoints, timeProvider);

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
    /// 画面へ送るマップの様子を読む。公開中の版と、消耗品の管理の装置とステーションを、受け渡しパネルで結び付ける（それぞれ別々に読む）。
    /// 外形のない受け渡しパネルの装置とステーションは、マップに描けないので入れない。
    /// </summary>
    private ScreenMapState ReadMap()
    {
        var map = _map.Current;
        var placements = map?.Drawing?.Placements;
        if (map is null || placements is null)
        {
            return ScreenMapState.Empty;
        }

        var endpoints = Consumables.Devices.Select(d => (Id: d.Value, Kind: "device", Consumables.DeviceEndpoint(d).Panel))
            .Concat(Consumables.Stations.Select(s => (Id: s.Value, Kind: "station", Consumables.StationEndpoint(s).Panel)));
        var equipment = endpoints
            .Where(e => placements.TryGetValue(e.Panel, out var placement) && placement.Outline is not null)
            .Select(e => new EquipmentView(e.Id, e.Kind, e.Panel.Value, OutlineView.From(placements[e.Panel].Outline!)))
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .ToArray();
        return new ScreenMapState(_map.CurrentVersion, map, equipment);
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
        screen.Attach(new ScreenSources(() => Dispatching.IsRunning, ReadAgvs, () => Notices.Recent, ReadMap, Consumables.ReadStatuses));
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
        app.MapConsumableEndpoints(Consumables, ToolPlanning, app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FlowHub.Api.ConsumableOperations"));
    }

    public void Dispose()
    {
        ToolPlanning.Dispose();
        Consumables.Dispose();
        Dispatching.Dispose();
        Traffic.Dispose();
    }

    /// <summary>
    /// 交通整理と配車の両方に登録されている AGV だけを登録する。片方にしかない AGV は、ログで警告し、画面で知らせる。
    /// </summary>
    private static async Task RegisterAgvsAsync(
        TrafficService traffic,
        TrafficStore trafficStore,
        DispatchingService dispatching,
        DispatchingStore dispatchingStore,
        INoticeBoard notices,
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
                await PostMismatchAsync(notices, agv.Id.ToString(), "走行", cancellationToken).ConfigureAwait(false);
                continue;
            }

            traffic.RegisterAgv(agv.Id, agv.Motion, agv.CanTravelSpecialTransfer);
            dispatching.RegisterAgv(agv.Id, dispatchingAgv.Carrier, dispatchingAgv.GoChargeBelowPercent, dispatchingAgv.ResumeAtPercent);
        }

        foreach (var id in inDispatching.Keys.OrderBy(id => id.Value, StringComparer.Ordinal))
        {
            LogMissingIn(logger, id.ToString(), "交通整理");
            await PostMismatchAsync(notices, id.ToString(), "配車", cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>登録の食い違いを画面で知らせる。</summary>
    /// <param name="registeredIn">登録されている側（画面の言葉で「走行」か「配車」）。</param>
    private static Task<Notice> PostMismatchAsync(INoticeBoard notices, string agv, string registeredIn, CancellationToken cancellationToken) =>
        notices.PostAsync(
            "AGV の登録が食い違っています",
            $"{agv} は{registeredIn}の側にだけ登録されているので、動かしていません。AGV と AGV の種類の登録の画面で、登録をそろえてください。",
            cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Agv} は {Missing} に登録されていないので、登録しませんでした。交通整理と配車の両方に登録してください。")]
    private static partial void LogMissingIn(ILogger logger, string agv, string missing);
}
