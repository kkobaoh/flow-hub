using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FlowHub.Api.Composition;
using FlowHub.Api.Database;
using FlowHub.Simulator;
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
            [new Connection(A, S, 800, new Heading(0), false), new Connection(A, D, 800, new Heading(90), false)]).Snapshot!);

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
    public async Task 交通整理と配車の両方に登録されているAGVだけを登録し食い違いを警告としてログに残す()
    {
        await SeedAsync(trafficAgvs: [Agv1, Agv2], dispatchingAgvs: [Agv1, Agv3]);

        var system = await StartAsync();

        Assert.Equal(Agv1, system.Traffic.GetAgv(Agv1).Id);
        Assert.Throws<InvalidOperationException>(() => system.Traffic.GetAgv(Agv2)); // 登録していない
        Assert.Throws<InvalidOperationException>(() => system.Traffic.GetAgv(Agv3));
        var warnings = _logs.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToArray();
        Assert.Contains(warnings, m => m.Contains("AGV-2", StringComparison.Ordinal) && m.Contains("配車", StringComparison.Ordinal));
        Assert.Contains(warnings, m => m.Contains("AGV-3", StringComparison.Ordinal) && m.Contains("交通整理", StringComparison.Ordinal));
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

    private static async Task<string?> StatusAsync(HttpClient client)
    {
        using var response = await client.GetAsync(new Uri("/system/status", UriKind.Relative), Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (string?)JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!["state"];
    }
}
