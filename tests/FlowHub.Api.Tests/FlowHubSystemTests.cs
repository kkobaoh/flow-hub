using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FlowHub.Api.Composition;
using FlowHub.Api.Composition.Screens;
using FlowHub.Api.Database;
using FlowHub.Simulator;
using FlowHub.Consumables;
using FlowHub.Consumables.Infrastructure;
using FlowHub.Dispatching;
using FlowHub.Dispatching.Infrastructure;
using FlowHub.Map;
using FlowHub.Map.Infrastructure;
using FlowHub.Routing;
using FlowHub.Traffic;
using FlowHub.Traffic.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlowHub.Api.Tests;

/// <summary>
/// 組み立て（起動して DB を開き、各コンテキストをつなぎ、AGV との通信の窓口を開く）のテスト。DB は一時フォルダーの SQLite。
/// </summary>
public sealed class FlowHubSystemTests : IAsyncDisposable
{
    private static readonly AgvId Agv1 = new("AGV-1");
    private static readonly AgvId Agv2 = new("AGV-2");
    private static readonly AgvId Agv3 = new("AGV-3");
    private static readonly PanelId A = new("A");
    private static readonly PanelId S = new("S");
    private static readonly PanelId D = new("D");
    private static readonly CarrierTypeId CarrierX = new("X");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "flowhub-tests", Guid.NewGuid().ToString("N"));
    private readonly RecordingLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;
    private FlowHubSystem? _system;
    private WebApplication? _app;
    private HttpClient? _client;

    public FlowHubSystemTests() => _loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(_logs));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private DatabaseOptions Database => new() { Provider = DatabaseProvider.Sqlite, SqliteDirectory = _directory };

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        _system?.Dispose();
        _loggerFactory.Dispose();
        _logs.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// 構成を DB に入れる。A（通常）に、S（ステーションの受け渡し）と D（装置の受け渡し）がつながる。
    /// </summary>
    /// <param name="trafficAgvs">交通整理に登録する AGV。</param>
    /// <param name="dispatchingAgvs">配車に登録する AGV。</param>
    private async Task SeedAsync(AgvId[] trafficAgvs, AgvId[] dispatchingAgvs)
    {
        var options = Database;
        var map = await MapDatabase.OpenAsync(b => Databases.Configure(b, options, MapDatabase.Schema), (c, t) => Databases.InitializeAsync(c, options, t), Token);
        map.Publish(MapSnapshot.Create(
            [new Panel(A, PanelType.Normal, false), new Panel(S, PanelType.Transfer, false), new Panel(D, PanelType.Transfer, false)],
            [new Connection(A, S, 800, new Heading(0), false), new Connection(A, D, 800, new Heading(90), false)],
            drawing: new MapDrawing(1000, new Dictionary<PanelId, PanelPlacement>
            {
                [A] = new(new GridPosition(0, 1), CrossOffset.None, null),
                [S] = new(new GridPosition(0, 0), CrossOffset.None, new Outline(-500, -1200, 2000, 1500)),
                [D] = new(new GridPosition(1, 1), CrossOffset.None, null),
            })).Snapshot!);

        var traffic = await TrafficDatabase.OpenAsync(b => Databases.Configure(b, options, TrafficDatabase.Schema), (c, t) => Databases.InitializeAsync(c, options, t), Token);
        await traffic.AddAgvTypeAsync("small", new MotionProfile(500, 250, 90), Token);
        foreach (var agv in trafficAgvs)
        {
            await traffic.AddAgvAsync(agv, "small", Token);
        }

        var dispatching = await DispatchingDatabase.OpenAsync(b => Databases.Configure(b, options, DispatchingDatabase.Schema), (c, t) => Databases.InitializeAsync(c, options, t), Token);
        await dispatching.AddAgvTypeAsync("small", goChargeBelowPercent: null, resumeAtPercent: null, Token);
        foreach (var agv in dispatchingAgvs)
        {
            await dispatching.AddAgvAsync(agv, "small", CarrierX, Token);
        }
    }

    private async Task<FlowHubSystem> StartAsync(bool startOnLaunch = false)
    {
        var settings = new FlowHubSettings(Database, new TrafficPlanningOptions(), new AgvEndpointOptions { CommandWait = TimeSpan.FromMilliseconds(300) }, new SimulatorOptions(), StartOnLaunch: startOnLaunch);
        _system = await FlowHubSystem.StartAsync(settings, TimeProvider.System, _loggerFactory, Token);
        return _system;
    }

    private async Task<HttpClient> ServeAsync(FlowHubSystem system)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        FlowHubSystem.AddScreenServices(builder.Services);
        _app = builder.Build();
        system.MapEndpoints(_app);
        await _app.StartAsync(Token);

        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _client = new HttpClient { BaseAddress = new Uri(address) };
        return _client;
    }

    private static async Task<JsonArray> CommandsAsync(HttpClient client, long lastReceived)
    {
        using var response = await client.GetAsync(new Uri($"/agv/AGV-1/commands?lastReceived={lastReceived}", UriKind.Relative), Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!["commands"]!.AsArray();
    }

    [Fact]
    public async Task 交通整理と配車の両方に登録されているAGVだけを登録し食い違いを警告としてログに残し画面で知らせる()
    {
        await SeedAsync(trafficAgvs: [Agv1, Agv2], dispatchingAgvs: [Agv1, Agv3]);

        var system = await StartAsync();

        Assert.Equal(Agv1, system.Traffic.GetAgv(Agv1).Id);
        Assert.Throws<InvalidOperationException>(() => system.Traffic.GetAgv(Agv2)); // 登録していない
        Assert.Throws<InvalidOperationException>(() => system.Traffic.GetAgv(Agv3));
        var warnings = _logs.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToArray();
        Assert.Contains(warnings, m => m.Contains("AGV-2", StringComparison.Ordinal) && m.Contains("配車", StringComparison.Ordinal));
        Assert.Contains(warnings, m => m.Contains("AGV-3", StringComparison.Ordinal) && m.Contains("交通整理", StringComparison.Ordinal));

        var notices = system.Notices.Recent;
        Assert.All(notices, n => Assert.Equal("AGV の登録が食い違っています", n.Title));
        Assert.Contains(notices, n => n.Detail.Contains("AGV-2", StringComparison.Ordinal) && n.Detail.Contains("走行", StringComparison.Ordinal));
        Assert.Contains(notices, n => n.Detail.Contains("AGV-3", StringComparison.Ordinal) && n.Detail.Contains("配車", StringComparison.Ordinal));
    }

    [Fact]
    public async Task 保存してあるコントロールステータスでAGVを登録する()
    {
        await SeedAsync(trafficAgvs: [Agv1], dispatchingAgvs: [Agv1]);

        var system = await StartAsync();

        Assert.Equal(ControlStatus.Detached, system.Traffic.GetAgv(Agv1).Control); // 新しく登録した AGV は切り離しから
    }

    [Fact]
    public async Task AGVからの通知のあとにイベントを届けて配車が搬送を割り当てる()
    {
        await SeedAsync(trafficAgvs: [Agv1], dispatchingAgvs: [Agv1]);
        var system = await StartAsync(startOnLaunch: true);
        var client = await ServeAsync(system);
        await system.Dispatching.RequestTransportAsync(new TransportOrder("test", ["C-1"], S, "station-1", D, "device-1", CarrierX, DateTimeOffset.MaxValue), Token);

        // 切り離しから、A に置いてオートに戻す（位置リセットの完了の通知は HTTP で届く）
        await system.Dispatching.ChangeControlStatusAsync(Agv1, ControlStatus.Auto, new Pose("A", 0), Token);
        var reset = (await CommandsAsync(client, -1)).Single()!;
        using var notified = await client.PostAsync(
            new Uri("/agv/AGV-1/notifications", UriKind.Relative),
            new StringContent($$"""{ "type": "positionResetCompleted", "number": {{reset["number"]}} }""", Encoding.UTF8, "application/json"),
            Token);
        var commands = await CommandsAsync(client, (long)reset["number"]!);

        Assert.Equal(HttpStatusCode.NoContent, notified.StatusCode);
        Assert.Equal("motion", (string?)commands.Single()!["type"]); // 位置リセットの完了を配車が受けて、S へ向かわせた
    }

    [Fact]
    public void 設定をappsettingsの形から読む()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:SqliteDirectory"] = "dev-data",
            ["Traffic:Planning:Horizon"] = "00:05:00",
            ["Traffic:AgvEndpoints:CommandWait"] = "00:00:30",
            ["Simulator:DeviceTransferDelay"] = "00:00:12",
            ["Simulator:ReplenishmentStation"] = "ST-2",
            ["System:StartOnLaunch"] = "true",
        }).Build();

        var settings = FlowHubSettings.Read(configuration, contentRoot: _directory);

        Assert.Equal(DatabaseProvider.Sqlite, settings.Database.Provider);
        Assert.Equal("dev-data", settings.Database.SqliteDirectory);
        Assert.Equal(TimeSpan.FromMinutes(5), settings.Planning.Horizon);
        Assert.Equal(TimeSpan.FromMilliseconds(200), settings.Planning.ImprovementBudget); // 書いていないものは既定値
        Assert.Equal(TimeSpan.FromSeconds(30), settings.AgvEndpoints.CommandWait);
        Assert.Equal(TimeSpan.FromSeconds(12), settings.Simulator.DeviceTransferDelay);
        Assert.Equal(TimeSpan.FromSeconds(10), settings.Simulator.StationTransferDelay);
        Assert.Equal("ST-2", settings.Simulator.ReplenishmentStation);
        Assert.True(settings.StartOnLaunch);
        Assert.False(FlowHubSettings.Read(new ConfigurationBuilder().Build(), _directory).StartOnLaunch); // 書かなければ停止から
    }

    // ---- 運転状態 ----

    [Fact]
    public async Task 起動したときは停止から始め搬送を割り当てない()
    {
        await SeedAsync(trafficAgvs: [Agv1], dispatchingAgvs: [Agv1]);
        var system = await StartAsync();

        Assert.False(system.Dispatching.IsRunning);
    }

    [Fact]
    public async Task 設定で起動したらすぐスタートにできる()
    {
        await SeedAsync(trafficAgvs: [Agv1], dispatchingAgvs: [Agv1]);
        var system = await StartAsync(startOnLaunch: true);

        Assert.True(system.Dispatching.IsRunning);
    }

    [Fact]
    public async Task HTTPで運転状態を切り替えて読める()
    {
        await SeedAsync(trafficAgvs: [Agv1], dispatchingAgvs: [Agv1]);
        var system = await StartAsync();
        var client = await ServeAsync(system);

        var initial = await StatusAsync(client);
        using var started = await client.PostAsync(new Uri("/system/start", UriKind.Relative), null, Token);
        var afterStart = await StatusAsync(client);
        using var stopped = await client.PostAsync(new Uri("/system/stop", UriKind.Relative), null, Token);
        var afterStop = await StatusAsync(client);

        Assert.Equal("stopped", initial);
        Assert.Equal(HttpStatusCode.NoContent, started.StatusCode);
        Assert.Equal("started", afterStart);
        Assert.Equal(HttpStatusCode.NoContent, stopped.StatusCode);
        Assert.Equal("stopped", afterStop);
    }

    [Fact]
    public async Task HTTPで搬送を取りやめられる()
    {
        await SeedAsync(trafficAgvs: [Agv1], dispatchingAgvs: [Agv1]);
        var system = await StartAsync();
        var client = await ServeAsync(system);
        var id = await system.Dispatching.RequestTransportAsync(new TransportOrder("test", ["C-1"], S, "station-1", D, "device-1", CarrierX, DateTimeOffset.MaxValue), Token);

        using var canceled = await client.PostAsync(new Uri($"/transports/{id.Value}/cancel", UriKind.Relative), null, Token);
        using var unknown = await client.PostAsync(new Uri("/transports/999/cancel", UriKind.Relative), null, Token);

        Assert.Equal(HttpStatusCode.OK, canceled.StatusCode);
        Assert.Equal("canceled", (string?)JsonNode.Parse(await canceled.Content.ReadAsStringAsync(Token))!["result"]);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    // ---- 画面からの AGV の操作 ----

    [Fact]
    public async Task 切り離しから位置を指定してオートに戻しAGVへ位置リセットを送る()
    {
        await SeedAsync(trafficAgvs: [Agv1], dispatchingAgvs: [Agv1]);
        var system = await StartAsync();
        var client = await ServeAsync(system);

        var (status, result) = await OperateAsync(client, "/agvs/AGV-1/control", """{ "status": "auto", "panel": "A", "heading": 90 }""");
        var commands = await CommandsAsync(client, -1);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("accepted", result);
        Assert.Equal("positionReset", (string?)commands.Single()!["type"]);
    }

    [Fact]
    public async Task 受け付けられない操作は理由の種類を返し形の正しくない頼みと知らないAGVは断る()
    {
        await SeedAsync(trafficAgvs: [Agv1], dispatchingAgvs: [Agv1]);
        var system = await StartAsync();
        var client = await ServeAsync(system);

        var notManual = await OperateAsync(client, "/agvs/AGV-1/manual/move", """{ "destination": "S" }"""); // 切り離し中
        var noPosition = await OperateAsync(client, "/agvs/AGV-1/control", """{ "status": "auto" }""");
        var offMap = await OperateAsync(client, "/agvs/AGV-1/control", """{ "status": "auto", "panel": "NOWHERE", "heading": 0 }""");
        var unknownStatus = await OperateAsync(client, "/agvs/AGV-1/control", """{ "status": "sleep" }""");
        var unknownAgv = await OperateAsync(client, "/agvs/AGV-9/emergency-stop", "{}");

        Assert.Equal((HttpStatusCode.Conflict, "notAllowed"), notManual);
        Assert.Equal(HttpStatusCode.BadRequest, noPosition.Status);
        Assert.Equal(HttpStatusCode.BadRequest, offMap.Status);
        Assert.Equal(HttpStatusCode.BadRequest, unknownStatus.Status);
        Assert.Equal(HttpStatusCode.NotFound, unknownAgv.Status);
    }

    [Fact]
    public async Task マニュアルのAGVへ行き先と着いたときの向きを送る()
    {
        await SeedAsync(trafficAgvs: [Agv1], dispatchingAgvs: [Agv1]);
        var system = await StartAsync();
        var client = await ServeAsync(system);
        await system.Dispatching.ChangeControlStatusAsync(Agv1, ControlStatus.Manual, new Pose("A", 0), Token);
        var reset = (await CommandsAsync(client, -1)).Single()!;
        using var completed = await client.PostAsync(
            new Uri("/agv/AGV-1/notifications", UriKind.Relative),
            new StringContent($$"""{ "type": "positionResetCompleted", "number": {{reset["number"]}} }""", Encoding.UTF8, "application/json"),
            Token);

        var already = await OperateAsync(client, "/agvs/AGV-1/manual/move", """{ "destination": "A", "arrival": 0 }""");
        var turn = await OperateAsync(client, "/agvs/AGV-1/manual/move", """{ "destination": "A", "arrival": 90 }""");

        Assert.Equal((HttpStatusCode.OK, "alreadyThere"), already);
        Assert.Equal((HttpStatusCode.OK, "accepted"), turn);
    }

    // ---- 消耗品の操作（装置とストッカー、ステーションの在庫の画面から） ----

    /// <summary>装置 DEV-1（スロット2つ）とステーション ST-1、品種 DR-5 と DR-6 を DB に入れる。</summary>
    private async Task SeedConsumablesAsync()
    {
        var options = Database;
        var consumables = await ConsumablesDatabase.OpenAsync(b => Databases.Configure(b, options, ConsumablesDatabase.Schema), (c, t) => Databases.InitializeAsync(c, options, t), Token);
        await consumables.AddProcessTypeAsync(new ProcessTypeSettings(new ProcessTypeId("P"), CarrierX, 2), Token);
        await consumables.AddVarietyAsync(new VarietyId("DR-5"), new ProcessTypeId("P"), Token);
        await consumables.AddVarietyAsync(new VarietyId("DR-6"), new ProcessTypeId("P"), Token);
        await consumables.AddStationAsync(new StationLayout(new StationId("ST-1"), S), Token);
        await consumables.AddDeviceAsync(new DeviceLayout(new DeviceId("DEV-1"), D, [2]), Token);
    }

    [Fact]
    public async Task ストッカーの設定を保存しスロットの数を超える設定は断る()
    {
        await SeedAsync(trafficAgvs: [], dispatchingAgvs: []);
        await SeedConsumablesAsync();
        var system = await StartAsync();
        var client = await ServeAsync(system);

        var saved = await OperateAsync(client, "/devices/DEV-1/stocker-setting", """{ "counts": [{ "variety": "DR-5", "count": 2 }] }""");
        var exceeds = await OperateAsync(client, "/devices/DEV-1/stocker-setting", """{ "counts": [{ "variety": "DR-5", "count": 2 }, { "variety": "DR-6", "count": 1 }] }""");
        var unknownVariety = await OperateAsync(client, "/devices/DEV-1/stocker-setting", """{ "counts": [{ "variety": "DR-9", "count": 1 }] }""");
        var negative = await OperateAsync(client, "/devices/DEV-1/stocker-setting", """{ "counts": [{ "variety": "DR-5", "count": -1 }] }""");
        var unknownDevice = await OperateAsync(client, "/devices/DEV-9/stocker-setting", """{ "counts": [] }""");

        Assert.Equal((HttpStatusCode.OK, "saved"), saved);
        Assert.Equal((HttpStatusCode.OK, "exceedsSlots"), exceeds);
        Assert.Equal(HttpStatusCode.BadRequest, unknownVariety.Status);
        Assert.Equal(HttpStatusCode.BadRequest, negative.Status);
        Assert.Equal(HttpStatusCode.NotFound, unknownDevice.Status);
        Assert.Equal([new StockerLine(new VarietyId("DR-5"), 2, 0)], system.Consumables.ReadStatuses().Devices.Single().Stocker);
    }

    [Fact]
    public async Task ステーションの消耗品の出庫を頼みステーションにないものは断る()
    {
        await SeedAsync(trafficAgvs: [], dispatchingAgvs: []);
        await SeedConsumablesAsync();
        var system = await StartAsync();
        var client = await ServeAsync(system);
        system.Consumables.RegisterConsumable(new ConsumableId("T-1"), new VarietyId("DR-5"), new ProcessTypeId("P"), ConsumableCondition.New, new AtStation(new StationId("ST-1")));
        system.Consumables.RegisterConsumable(new ConsumableId("T-2"), new VarietyId("DR-5"), new ProcessTypeId("P"), ConsumableCondition.New, new InStocker(new DeviceId("DEV-1"), new SlotRef(1, 1)));

        var requested = await OperateAsync(client, "/consumables/T-1/ship", "{}");
        var notAtStation = await OperateAsync(client, "/consumables/T-2/ship", "{}");
        var unknown = await OperateAsync(client, "/consumables/T-9/ship", "{}");

        Assert.Equal((HttpStatusCode.OK, "requested"), requested);
        Assert.Equal((HttpStatusCode.OK, "notAtStation"), notAtStation);
        Assert.Equal((HttpStatusCode.OK, "notAtStation"), unknown); // 登録されていない ID も、ステーションにないものとして断る
    }

    [Fact]
    public async Task 要確認の消耗品を直しもう要確認でなければ断る()
    {
        await SeedAsync(trafficAgvs: [], dispatchingAgvs: []);
        await SeedConsumablesAsync();
        var system = await StartAsync();
        var client = await ServeAsync(system);
        await system.Consumables.ReportReadAtStationAsync(new StationId("ST-1"), new ConsumableId("X-1"), Token); // 登録されていない ID は要確認

        var corrected = await OperateAsync(client, "/consumables/X-1/correct", """{ "variety": "DR-5", "condition": "used" }""");
        var again = await OperateAsync(client, "/consumables/X-1/correct", """{ "variety": "DR-5", "condition": "new" }""");
        var unknownVariety = await OperateAsync(client, "/consumables/X-1/correct", """{ "variety": "DR-9", "condition": "new" }""");
        var unknownCondition = await OperateAsync(client, "/consumables/X-1/correct", """{ "variety": "DR-5", "condition": "broken" }""");

        Assert.Equal((HttpStatusCode.OK, "corrected"), corrected);
        Assert.Equal((HttpStatusCode.OK, "notNeedingCheck"), again);
        Assert.Equal(HttpStatusCode.BadRequest, unknownVariety.Status);
        Assert.Equal(HttpStatusCode.BadRequest, unknownCondition.Status);
        Assert.Equal(ConsumableCondition.Used, system.Consumables.GetConsumable(new ConsumableId("X-1")).Condition);
    }

    [Fact]
    public async Task 届ける消耗品を指名して取り消し指名できないものは断る()
    {
        await SeedAsync(trafficAgvs: [], dispatchingAgvs: []);
        await SeedConsumablesAsync();
        var system = await StartAsync();
        var client = await ServeAsync(system);
        system.Consumables.RegisterConsumable(new ConsumableId("T-1"), new VarietyId("DR-5"), new ProcessTypeId("P"), ConsumableCondition.New, new AtStation(new StationId("ST-1")));
        system.Consumables.RegisterConsumable(new ConsumableId("U-1"), new VarietyId("DR-5"), new ProcessTypeId("P"), ConsumableCondition.Used, new AtStation(new StationId("ST-1")));

        var designated = await OperateAsync(client, "/consumables/T-1/designate", """{ "device": "DEV-1" }""");
        var used = await OperateAsync(client, "/consumables/U-1/designate", """{ "device": "DEV-1" }""");
        var noDevice = await OperateAsync(client, "/consumables/T-1/designate", "{}");
        Assert.Equal(new DeviceId("DEV-1"), system.Consumables.ReadConsumables().Single(c => c.Id.Value == "T-1").DesignatedFor);
        var canceled = await OperateAsync(client, "/consumables/T-1/cancel-designation", "{}");

        Assert.Equal((HttpStatusCode.OK, "designated"), designated);
        Assert.Equal((HttpStatusCode.OK, "notDesignatable"), used);
        Assert.Equal(HttpStatusCode.BadRequest, noDevice.Status);
        Assert.Equal((HttpStatusCode.OK, "canceled"), canceled);
        Assert.Null(system.Consumables.ReadConsumables().Single(c => c.Id.Value == "T-1").DesignatedFor);
    }

    [Fact]
    public async Task 在庫を手入力で登録し直し消す()
    {
        await SeedAsync(trafficAgvs: [], dispatchingAgvs: []);
        await SeedConsumablesAsync();
        var system = await StartAsync();
        var client = await ServeAsync(system);
        ConsumableRecord Record() => system.Consumables.ReadConsumables().Single();

        var registered = await OperateAsync(client, "/consumables", """{ "id": "T-1", "variety": "DR-5", "condition": "new", "location": { "kind": "station", "station": "ST-1" } }""");
        var again = await OperateAsync(client, "/consumables", """{ "id": "T-1", "variety": "DR-5", "condition": "new", "location": { "kind": "station", "station": "ST-1" } }""");
        var unknownPlace = await OperateAsync(client, "/consumables", """{ "id": "T-2", "variety": "DR-5", "condition": "new", "location": { "kind": "moon" } }""");
        Assert.Equal(new AtStation(new StationId("ST-1")), Record().Location);

        var edited = await OperateAsync(client, "/consumables/T-1/edit", """{ "variety": "DR-6", "condition": "used", "location": { "kind": "slot", "device": "DEV-1", "stocker": 1, "slot": 2 } }""");
        Assert.Equal((new VarietyId("DR-6"), ConsumableCondition.Used, (ConsumableLocation)new InStocker(new DeviceId("DEV-1"), new SlotRef(1, 2))), (Record().Variety, Record().Condition, Record().Location));
        var deleted = await OperateAsync(client, "/consumables/T-1/delete", "{}");
        var gone = await OperateAsync(client, "/consumables/T-1/delete", "{}");

        Assert.Equal((HttpStatusCode.OK, "saved"), registered);
        Assert.Equal((HttpStatusCode.OK, "alreadyRegistered"), again);
        Assert.Equal(HttpStatusCode.BadRequest, unknownPlace.Status);
        Assert.Equal((HttpStatusCode.OK, "saved"), edited);
        Assert.Equal((HttpStatusCode.OK, "saved"), deleted);
        Assert.Equal((HttpStatusCode.OK, "notFound"), gone);
        Assert.Empty(system.Consumables.ReadConsumables());
    }

    private static async Task<(HttpStatusCode Status, string? Result)> OperateAsync(HttpClient client, string path, string body)
    {
        using var response = await client.PostAsync(new Uri(path, UriKind.Relative), new StringContent(body, Encoding.UTF8, "application/json"), Token);
        var text = await response.Content.ReadAsStringAsync(Token);
        return (response.StatusCode, text.TrimStart().StartsWith('{') ? (string?)JsonNode.Parse(text)!["result"] : null);
    }

    // ---- 画面へのリアルタイムの知らせ（SignalR） ----

    [Fact]
    public async Task 画面がつないだときに今の運転状態が届く()
    {
        await SeedAsync(trafficAgvs: [Agv1], dispatchingAgvs: [Agv1]);
        var system = await StartAsync(startOnLaunch: true);
        var client = await ServeAsync(system);
        var received = new SnapshotQueue<SystemStatusSnapshot>();
        await using var screen = await ConnectScreenAsync(client, received);

        var first = await received.NextAsync();

        Assert.Equal("started", first.State);
    }

    [Fact]
    public async Task 画面がつないだときに公開中のマップと外形のある装置とステーションが届く()
    {
        await SeedAsync(trafficAgvs: [Agv1], dispatchingAgvs: [Agv1]);
        var options = Database;
        var consumables = await ConsumablesDatabase.OpenAsync(b => Databases.Configure(b, options, ConsumablesDatabase.Schema), (c, t) => Databases.InitializeAsync(c, options, t), Token);
        await consumables.AddStationAsync(new StationLayout(new StationId("ST-1"), S), Token);
        await consumables.AddDeviceAsync(new DeviceLayout(new DeviceId("DEV-1"), D, [2]), Token); // D には外形がない
        var system = await StartAsync();
        var client = await ServeAsync(system);
        var map = new SnapshotQueue<MapViewSnapshot>();
        await using var screen = await ConnectScreenAsync(client, new SnapshotQueue<SystemStatusSnapshot>(), map: map);

        var first = await map.NextAsync();

        Assert.Equal(1, first.MapVersion);
        Assert.Equal(1000, first.PanelSizeMm);
        Assert.Equal(["A", "S", "D"], first.Panels.Select(p => p.Id));
        Assert.Equal(new PanelView("S", "Transfer", false, null, 0, 0, 0, 0), first.Panels[1]);
        Assert.Equal(2, first.Connections.Count);
        Assert.Equal([new EquipmentView("ST-1", "station", "S", new OutlineView(-500, -1200, 2000, 1500))], first.Equipment);
    }

    [Fact]
    public async Task 運転状態が変わったら新しい番号で全員に届く()
    {
        await SeedAsync(trafficAgvs: [Agv1], dispatchingAgvs: [Agv1]);
        var system = await StartAsync(startOnLaunch: true);
        var client = await ServeAsync(system);
        var receivedA = new SnapshotQueue<SystemStatusSnapshot>();
        var receivedB = new SnapshotQueue<SystemStatusSnapshot>();
        await using var screenA = await ConnectScreenAsync(client, receivedA);
        await using var screenB = await ConnectScreenAsync(client, receivedB);
        var first = await receivedA.NextAsync();
        await receivedB.NextAsync();

        using var stopped = await client.PostAsync(new Uri("/system/stop", UriKind.Relative), null, Token);
        var changedA = await receivedA.NextAsync();
        var changedB = await receivedB.NextAsync();

        Assert.Equal("stopped", changedA.State);
        Assert.True(changedA.Version > first.Version);
        Assert.Equal(changedA, changedB);
    }

    [Fact]
    public async Task 画面がつないだときにAGVの様子が届き充電残量が変わったら新しい番号で届く()
    {
        await SeedAsync(trafficAgvs: [Agv1], dispatchingAgvs: [Agv1]);
        var system = await StartAsync();
        var client = await ServeAsync(system);
        var agvs = new SnapshotQueue<AgvsWire>();
        await using var screen = await ConnectScreenAsync(client, new SnapshotQueue<SystemStatusSnapshot>(), agvs);

        var first = await agvs.NextAsync();
        using var notified = await client.PostAsync(
            new Uri("/agv/AGV-1/notifications", UriKind.Relative),
            new StringContent("""{ "type": "battery", "percent": 57 }""", Encoding.UTF8, "application/json"),
            Token);
        var changed = await agvs.NextAsync();

        var agv = Assert.Single(first.Agvs);
        Assert.Equal(new AgvSnapshot("AGV-1", null, null, "unregistered", "detached", false, null), agv); // 新しく登録した AGV は、切り離しで位置がわからない
        Assert.True(changed.Version > first.Version);
        Assert.Equal(57, Assert.Single(changed.Agvs).BatteryPercent);
    }

    private static async Task<HubConnection> ConnectScreenAsync(HttpClient client, SnapshotQueue<SystemStatusSnapshot> received, SnapshotQueue<AgvsWire>? agvs = null, SnapshotQueue<MapViewSnapshot>? map = null)
    {
        var connection = new HubConnectionBuilder().WithUrl(new Uri(client.BaseAddress!, "/hubs/screen")).Build();
        connection.On<SystemStatusSnapshot>("systemStatus", received.Add);
        connection.On<AgvsWire>("agvs", snapshot => agvs?.Add(snapshot));
        connection.On<MapViewSnapshot>("map", snapshot => map?.Add(snapshot));
        await connection.StartAsync(Token);
        return connection;
    }

    /// <summary>画面に届いた AGV の様子（受け取るための形）。</summary>
    private sealed record AgvsWire(long Version, AgvSnapshot[] Agvs);

    /// <summary>届いた様子を、届いた順に取り出す。</summary>
    private sealed class SnapshotQueue<T>
    {
        private readonly System.Threading.Channels.Channel<T> _channel = System.Threading.Channels.Channel.CreateUnbounded<T>();

        public void Add(T snapshot) => _channel.Writer.TryWrite(snapshot);

        public async Task<T> NextAsync()
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            return await _channel.Reader.ReadAsync(timeout.Token);
        }
    }

    private static async Task<string?> StatusAsync(HttpClient client)
    {
        using var response = await client.GetAsync(new Uri("/system/status", UriKind.Relative), Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (string?)JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!["state"];
    }
}
