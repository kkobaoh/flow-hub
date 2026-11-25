using System.Text.Json.Nodes;
using FlowHub.Api.Composition;
using FlowHub.Api.Database;
using FlowHub.Api.InitialData;
using FlowHub.Consumables;
using FlowHub.Consumables.Application;
using FlowHub.Map;
using FlowHub.Map.Infrastructure;
using FlowHub.Simulator;
using FlowHub.Traffic;
using FlowHub.Traffic.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FlowHub.Api.Tests;

/// <summary>
/// 開発用の初期データ（設定でファイルを指定し、4つの DB がすべて空なら入れる）のテスト。DB は一時フォルダーの SQLite。
/// </summary>
public sealed class InitialDataTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "flowhub-tests", Guid.NewGuid().ToString("N"));
    private readonly RecordingLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly List<FlowHubSystem> _systems = [];

    public InitialDataTests()
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(_logs));
        Directory.CreateDirectory(_directory);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private DatabaseOptions Database => new() { Provider = DatabaseProvider.Sqlite, SqliteDirectory = Path.Combine(_directory, "db") };

    public void Dispose()
    {
        foreach (var system in _systems)
        {
            system.Dispose();
        }

        _loggerFactory.Dispose();
        _logs.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    /// <summary>リポジトリに同梱している開発用の初期データ。</summary>
    private static string BundledFile
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FlowHub.slnx")))
            {
                directory = directory.Parent;
            }

            return Path.Combine(directory!.FullName, "src", "FlowHub.Api", "InitialData", "initial-data.json");
        }
    }

    /// <summary>同梱の初期データを書き換えて、一時フォルダーに置く。</summary>
    private string WriteModified(Action<JsonNode> modify)
    {
        var json = JsonNode.Parse(File.ReadAllText(BundledFile))!;
        modify(json);
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json.ToJsonString());
        return path;
    }

    private async Task<FlowHubSystem> StartAsync(string? initialData, TimeProvider? clock = null, bool startOnLaunch = false)
    {
        var settings = new FlowHubSettings(Database, new TrafficPlanningOptions(), new AgvEndpointOptions(), new SimulatorOptions(), initialData, startOnLaunch);
        var system = await FlowHubSystem.StartAsync(settings, clock ?? TimeProvider.System, _loggerFactory, Token);
        _systems.Add(system);
        return system;
    }

    private async Task<MapSnapshot?> OpenMapAsync()
    {
        var options = Database;
        var map = await MapDatabase.OpenAsync(b => Databases.Configure(b, options, MapDatabase.Schema), (c, t) => Databases.InitializeAsync(c, options, t), Token);
        return map.Current;
    }

    [Fact]
    public async Task 全部のDBが空なら初期データを入れる()
    {
        var system = await StartAsync(BundledFile);

        Assert.Equal(ControlStatus.Detached, system.Traffic.GetAgv(new AgvId("AGV-1")).Control); // 新しく登録した AGV は切り離しから
        Assert.Equal(ControlStatus.Detached, system.Traffic.GetAgv(new AgvId("AGV-2")).Control);
        Assert.Equal([new DeviceId("DEV-1"), new DeviceId("DEV-2")], system.Consumables.Devices);
        Assert.Equal([new StationId("ST-1")], system.Consumables.Stations);
        Assert.Equal(13, (await OpenMapAsync())!.Panels.Count);
        Assert.Equal("標準の AGV", Assert.Single(system.Fleet.Current.Types).Name); // 初期データには、種類の名前も書ける
    }

    [Fact]
    public async Task 初期データのパネルの位置と受け渡しパネルの外形を入れる()
    {
        await StartAsync(BundledFile);

        var drawing = (await OpenMapAsync())!.Drawing!;
        Assert.Equal(1000, drawing.PanelSizeMm);
        Assert.Equal(new GridPosition(2, 0), drawing.Placements[new PanelId("ST")].Position);
        Assert.Equal(new Outline(3050, 2900, 900, 2100), drawing.Placements[new PanelId("D1")].Outline);
        Assert.Null(drawing.Placements[new PanelId("P1")].Outline);
    }

    [Fact]
    public async Task 特殊消耗品受け渡しパネルとキャリアを下ろす角度を入れられ装置の受け渡しパネルに使える()
    {
        var path = WriteModified(json =>
        {
            json["map"]!["panels"]!.AsArray().Add(new JsonObject { ["id"] = "X", ["type"] = "SpecialTransfer", ["carrierLoweringHeading"] = 45, ["column"] = 5, ["row"] = 2 });
            json["map"]!["connections"]!.AsArray().Add(new JsonObject { ["from"] = "P5", ["to"] = "X", ["distanceMm"] = 1000, ["heading"] = 90 });
            json["consumables"]!["devices"]![0]!["transferPanel"] = "X";
        });

        var system = await StartAsync(path);

        var panel = (await OpenMapAsync())!.Panels.Single(p => p.Id == new PanelId("X"));
        Assert.Equal((PanelType.SpecialTransfer, new Heading(45)), (panel.Type, panel.CarrierLoweringHeading));
        Assert.Equal(new PanelId("X"), system.Consumables.DeviceEndpoint(new DeviceId("DEV-1")).Panel);
    }

    [Fact]
    public async Task AGVの種類に特殊消耗品受け渡しパネルを走れるかを書け省略したら走れない()
    {
        var path = WriteModified(json =>
        {
            var types = json["agvTypes"]!.AsArray();
            types.Add(new JsonObject
            {
                ["id"] = "special",
                ["forwardSpeedMmPerSecond"] = 500,
                ["backwardSpeedMmPerSecond"] = 250,
                ["rotationSpeedDegreesPerSecond"] = 90,
                ["canTravelSpecialTransfer"] = true,
            });
            json["agvs"]![1]!["type"] = "special";
        });

        var system = await StartAsync(path);

        Assert.False(system.Traffic.GetAgv(new AgvId("AGV-1")).CanTravelSpecialTransfer);
        Assert.True(system.Traffic.GetAgv(new AgvId("AGV-2")).CanTravelSpecialTransfer);
    }

    [Fact]
    public async Task 再起動したときはDBが空でないので入れ直さない()
    {
        using (await StartAsync(BundledFile))
        {
        }

        _systems.Clear();
        var restarted = await StartAsync(BundledFile);

        Assert.Equal([new DeviceId("DEV-1"), new DeviceId("DEV-2")], restarted.Consumables.Devices); // 二重に入れると、登録が重なって失敗する
    }

    [Fact]
    public async Task DBが1つでも空でなければ何も入れない()
    {
        // マップの DB だけに中身がある
        var options = Database;
        var map = await MapDatabase.OpenAsync(b => Databases.Configure(b, options, MapDatabase.Schema), (c, t) => Databases.InitializeAsync(c, options, t), Token);
        map.Publish(MapSnapshot.Create(
            [new Panel(new PanelId("X"), PanelType.Normal, false)],
            [],
            drawing: new MapDrawing(1000, new Dictionary<PanelId, PanelPlacement> { [new PanelId("X")] = new(new GridPosition(0, 0), CrossOffset.None, null) })).Snapshot!);

        var system = await StartAsync(BundledFile);

        Assert.Throws<InvalidOperationException>(() => system.Traffic.GetAgv(new AgvId("AGV-1"))); // AGV は入れていない
        Assert.Empty(system.Consumables.Devices);
        Assert.Equal(new PanelId("X"), Assert.Single((await OpenMapAsync())!.Panels).Id);
    }

    [Fact]
    public async Task 初期データを指定しなければ何も入れない()
    {
        var system = await StartAsync(initialData: null);

        Assert.Empty(system.Consumables.Devices);
        Assert.Null(await OpenMapAsync());
    }

    [Fact]
    public async Task 指定したファイルがなければログに出して起動しない()
    {
        var missing = Path.Combine(_directory, "missing.json");

        var error = await Assert.ThrowsAsync<InitialDataException>(() => StartAsync(missing));

        Assert.Contains("missing.json", error.Message, StringComparison.Ordinal);
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("missing.json", StringComparison.Ordinal));
    }

    public static TheoryData<string, string> BrokenData => new()
    {
        { "AGV の種類がない", "agvs.0.type=unknown" },
        { "マップの検証に通らない", "map.connections.0.distanceMm=0" },
        { "同じ格子に2枚のパネル", "map.panels.1.column=1" },
        { "十字の交点の間が短すぎる", "map.panelSizeMm=600" },
        { "パネルの位置がない", "map.panels.0.row=null" },
        { "パネルの一辺の長さがない", "map.panelSizeMm=null" },
        { "受け渡しパネルがマップにない", "consumables.devices.0.transferPanel=NOWHERE" },
        { "品種の加工の種類がない", "consumables.varieties.0.processType=unknown" },
        { "ストッカーの設定の品種がない", "consumables.stockerSettings.1.counts.DR-9=1" },
        { "ストッカーの設定の装置がない", "consumables.stockerSettings.1.device=DEV-9" },
        { "入庫の予定の品種がない", "consumables.expectedArrivals+C-1=unknown" },
        { "AGV の ID が重なる", "agvs.1.id=AGV-1" },
        { "JSON として読めない", "!" },
    };

    [Theory]
    [MemberData(nameof(BrokenData))]
    public async Task 中身が正しくなければ何も入れずにログに出して起動しない(string reason, string change)
    {
        var path = change == "!" ? WriteBroken() : WriteModified(json => Apply(json, change));

        await Assert.ThrowsAsync<InitialDataException>(() => StartAsync(path));
        var restarted = await StartAsync(initialData: null);

        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Error);
        Assert.Null(await OpenMapAsync()); // 何も入れていない（{reason}）
        Assert.Empty(restarted.Consumables.Devices);
        _ = reason;
    }

    private string WriteBroken()
    {
        var path = Path.Combine(_directory, "broken.json");
        File.WriteAllText(path, "{ \"map\": ");
        return path;
    }

    /// <summary>「a.b.0.c=値」の形で、JSON の1か所を書き換える。「a.b+キー=値」は、配列に要素を足す（入庫の予定の形）。</summary>
    private static void Apply(JsonNode json, string change)
    {
        var (path, value) = (change[..change.IndexOf('=', StringComparison.Ordinal)], change[(change.IndexOf('=', StringComparison.Ordinal) + 1)..]);
        if (path.Contains('+', StringComparison.Ordinal))
        {
            var (arrayPath, key) = (path[..path.IndexOf('+', StringComparison.Ordinal)], path[(path.IndexOf('+', StringComparison.Ordinal) + 1)..]);
            Navigate(json, arrayPath.Split('.')).AsArray().Add(new JsonObject { ["consumable"] = key, ["variety"] = value });
            return;
        }

        var parts = path.Split('.');
        var parent = Navigate(json, parts[..^1]);
        JsonNode? newValue = value == "null" ? null : int.TryParse(value, out var number) ? JsonValue.Create(number) : JsonValue.Create(value);
        if (parent is JsonArray array)
        {
            array[int.Parse(parts[^1], System.Globalization.CultureInfo.InvariantCulture)] = newValue;
        }
        else
        {
            parent[parts[^1]] = newValue;
        }
    }

    private static JsonNode Navigate(JsonNode node, IEnumerable<string> parts) =>
        parts.Aggregate(node, (current, part) => int.TryParse(part, out var index) && current is JsonArray array ? array[index]! : current[part]!);

    [Fact]
    public async Task 起動しただけでは不足を判断せず補充も頼まない()
    {
        var clock = new ManualTimeProvider(Start);
        var system = await StartAsync(BundledFile, clock);

        clock.Advance(TimeSpan.FromSeconds(60));
        var arrived = await EventuallyAsync(() => TryGet(system, "SIM-DR-8-1") is not null, attempts: 10);

        Assert.False(arrived);
    }

    [Fact]
    public async Task スタートしたら不足の判断を1回行い足りない品種の補充を頼む()
    {
        var clock = new ManualTimeProvider(Start);
        var system = await StartAsync(BundledFile, clock, startOnLaunch: true);

        clock.Advance(TimeSpan.FromSeconds(60)); // シミュレーターの補充の待ち時間
        var arrived = await EventuallyAsync(() => TryGet(system, "SIM-DR-8-1") is not null);

        Assert.True(arrived);
        Assert.Equal(new VarietyId("DR-8"), TryGet(system, "SIM-DR-8-1")!.Variety);
        Assert.NotNull(TryGet(system, "SIM-DR-5-1"));
    }

    private static ConsumableView? TryGet(FlowHubSystem system, string id)
    {
        try
        {
            return system.Consumables.GetConsumable(new ConsumableId(id));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>別のスレッドで進む処理（シミュレーターの通知）を、しばらく待つ。</summary>
    private static async Task<bool> EventuallyAsync(Func<bool> condition, int attempts = 100)
    {
        for (var i = 0; i < attempts; i++)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(50, Token);
        }

        return condition();
    }

    [Fact]
    public void 設定の初期データのファイルはアプリのフォルダーからの場所として読む()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["InitialData:Path"] = "InitialData/initial-data.json",
        }).Build();

        var settings = FlowHubSettings.Read(configuration, contentRoot: _directory);
        var none = FlowHubSettings.Read(new ConfigurationBuilder().Build(), contentRoot: _directory);

        Assert.Equal(Path.Combine(_directory, "InitialData/initial-data.json"), settings.InitialDataPath);
        Assert.Null(none.InitialDataPath);
    }
}
