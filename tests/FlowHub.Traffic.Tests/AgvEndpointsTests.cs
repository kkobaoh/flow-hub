using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FlowHub.BuildingBlocks;
using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic.Application;
using FlowHub.Traffic.Domain;
using FlowHub.Traffic.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace FlowHub.Traffic.Tests;

/// <summary>
/// AGV との通信の HTTP の窓口（docs/agv-protocol.md）のテスト。ローカルにサーバーを立てて、HTTP で確かめる。
/// </summary>
public sealed class AgvEndpointsTests : IAsyncDisposable
{
    private static readonly AgvId Agv = new("AGV-1");
    private static readonly PanelId B = new("B");
    private static readonly PanelId T = new("T");
    private static readonly MotionProfile Motion = new(500, 250, 90);
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly InMemoryMapStore _map = new();
    private readonly RecordingPublisher _events = new();
    private readonly RecordingRegistrations _registrations = new();
    private readonly TrafficService _service;
    private WebApplication? _app;
    private HttpClient? _client;

    public AgvEndpointsTests()
    {
        // A →(0度) B →(0度) T（受け渡しパネル）
        _map.Publish(MapSnapshot.Create(
            [
                new Panel(new PanelId("A"), PanelType.Normal, false),
                new Panel(B, PanelType.Normal, false),
                new Panel(T, PanelType.Transfer, true),
            ],
            [
                new Connection(new PanelId("A"), B, 800, new Heading(0), OneWay: false),
                new Connection(B, T, 800, new Heading(0), OneWay: false),
            ]).Snapshot!);
        _service = new TrafficService(_map, _events, TimeProvider.System, autoRegistrations: _registrations);
        _service.RegisterAgv(Agv, Motion);
    }

    private sealed class RecordingRegistrations : IAgvAutoRegistrationStore
    {
        public List<AgvId> Added { get; } = [];

        public Task AddUnconfiguredAgvAsync(AgvId id, CancellationToken cancellationToken)
        {
            Added.Add(id);
            return Task.CompletedTask;
        }
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

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        _service.Dispose();
    }

    /// <summary>
    /// サーバーを立てる。コマンドがないときに待つ時間は、テストのために短くする。
    /// </summary>
    private async Task<HttpClient> StartAsync(TimeSpan? commandWait = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();
        _app.MapAgvEndpoints(_service, new AgvEndpointOptions { CommandWait = commandWait ?? TimeSpan.FromMilliseconds(300) }, TimeProvider.System);
        await _app.StartAsync(None);

        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _client = new HttpClient { BaseAddress = new Uri(address) };
        return _client;
    }

    /// <summary>
    /// AGV を位置リセットして、待機中にする（番号 1 は位置リセット）。
    /// </summary>
    private async Task ReadyAtAsync(string panel, int heading)
    {
        await _service.ResetPositionAsync(Agv, new Pose(panel, heading), None);
        await _service.HandleCommandRequestAsync(Agv, -1, None);
        await _service.HandlePositionResetCompletedAsync(Agv, 1, None);
    }

    private static async Task<JsonNode> CommandsAsync(HttpClient client, long lastReceived)
    {
        using var response = await client.GetAsync(new Uri($"/agv/AGV-1/commands?lastReceived={lastReceived}", UriKind.Relative), None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(None))!["commands"]!;
    }

    private static Task<HttpResponseMessage> NotifyAsync(HttpClient client, string json, string agv = "AGV-1") =>
        client.PostAsync(
            new Uri($"/agv/{agv}/notifications", UriKind.Relative),
            new StringContent(json, Encoding.UTF8, "application/json"),
            None);

    private static void AssertJson(string expected, JsonNode? actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), actual), $"期待：{expected}{Environment.NewLine}実際：{actual?.ToJsonString()}");

    // ---- コマンドを取りに来る ----

    [Fact]
    public async Task 位置リセットのコマンドを番号とパネルと角度で返す()
    {
        var client = await StartAsync();
        await _service.ResetPositionAsync(Agv, new Pose("A", 90), None);

        var commands = await CommandsAsync(client, -1);

        AssertJson("""[{ "number": 1, "type": "positionReset", "panel": "A", "heading": 90 }]""", commands);
    }

    [Fact]
    public async Task コマンドがなければ待ってから空の配列を返す()
    {
        var client = await StartAsync(TimeSpan.FromMilliseconds(300));
        await ReadyAtAsync("A", 0);
        var watch = Stopwatch.StartNew();

        var commands = await CommandsAsync(client, 1);

        AssertJson("[]", commands);
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(250), $"待った時間：{watch.Elapsed}");
    }

    [Fact]
    public async Task 待っている間にコマンドができたらすぐに返す()
    {
        var client = await StartAsync(TimeSpan.FromSeconds(10));
        await ReadyAtAsync("A", 0);
        var watch = Stopwatch.StartNew();

        var polling = CommandsAsync(client, 1);
        await Task.Delay(200, None);
        await _service.MoveAgvAsync(Agv, B, null, "test", None);
        var commands = await polling;

        AssertJson("""[{ "number": 2, "type": "motion", "steps": [{ "action": "forward", "distanceMm": 800, "panel": "B" }] }]""", commands);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"待った時間：{watch.Elapsed}");
    }

    [Fact]
    public async Task 後退の動作を返す()
    {
        // B（0度）から A へは、向きを変えずに後退するのが一番安い
        var client = await StartAsync();
        await ReadyAtAsync("B", 0);
        await _service.MoveAgvAsync(Agv, new PanelId("A"), null, "test", None);

        var commands = await CommandsAsync(client, 1);

        AssertJson("""[{ "number": 2, "type": "motion", "steps": [{ "action": "backward", "distanceMm": 800, "panel": "A" }] }]""", commands);
    }

    [Fact]
    public async Task 回転の動作を返す()
    {
        // A（90度）から B へは、まず 0度へ回転する。区間は回転で区切るので、最初の動作コマンドは回転だけ
        var client = await StartAsync();
        await ReadyAtAsync("A", 90);
        await _service.MoveAgvAsync(Agv, B, null, "test", None);

        var commands = await CommandsAsync(client, 1);

        AssertJson("""[{ "number": 2, "type": "motion", "steps": [{ "action": "rotate", "heading": 0 }] }]""", commands);
    }

    [Fact]
    public async Task 角度を指定した回転の動作を返す()
    {
        var client = await StartAsync();
        await ReadyAtAsync("A", 90);
        await _service.ChangeControlStatusAsync(Agv, ControlStatus.Manual, position: null, None);
        await _service.ManualStepAsync(Agv, new ManualFreeRotate(45), None);

        var commands = await CommandsAsync(client, 1);

        AssertJson("""[{ "number": 2, "type": "motion", "steps": [{ "action": "rotate_free", "heading": 45 }] }]""", commands);
    }

    [Fact]
    public void 特殊な前進と後退と回転の動作の形()
    {
        var steps = new TravelCommand[] { new ForwardCommand(800, B, Special: true), new BackwardCommand(800, T, Special: true), new RotateSpecialCommand(215) };

        var json = AgvMessages.Commands([new MotionCommand(5, steps)]);

        AssertJson(
            """[{ "number": 5, "type": "motion", "steps": [{ "action": "forward_special", "distanceMm": 800, "panel": "B" }, { "action": "backward_special", "distanceMm": 800, "panel": "T" }, { "action": "rotate_special", "heading": 215 }] }]""",
            json["commands"]);
    }

    [Fact]
    public async Task キャリアと緊急停止とアラームクリアのコマンドを返す()
    {
        var client = await StartAsync();
        await ReadyAtAsync("T", 0);
        _map.SetCarrierHeight(T, 120, DateTimeOffset.UtcNow);

        await _service.LowerCarrierAsync(Agv, "test", None);
        AssertJson("""[{ "number": 2, "type": "lowerCarrier", "heightMm": 120 }]""", await CommandsAsync(client, 1));

        await _service.EmergencyStopAsync(Agv, None);
        AssertJson("""[{ "number": 3, "type": "emergencyStop" }]""", await CommandsAsync(client, 2));

        await _service.ClearAlarmAsync(Agv, None);
        AssertJson("""[{ "number": 4, "type": "alarmClear" }]""", await CommandsAsync(client, 3));
    }

    [Fact]
    public async Task 高さが決まっていなければキャリアを下げるコマンドに高さを入れずキャリアを上げるコマンドも返す()
    {
        var client = await StartAsync();
        await ReadyAtAsync("T", 0);

        await _service.LowerCarrierAsync(Agv, "test", None);
        AssertJson("""[{ "number": 2, "type": "lowerCarrier" }]""", await CommandsAsync(client, 1));
        Assert.Equal(HttpStatusCode.NoContent, (await NotifyAsync(client, """{ "type": "carrierLowered", "number": 2 }""")).StatusCode);

        await _service.RaiseCarrierAsync(Agv, "test", None);
        AssertJson("""[{ "number": 3, "type": "raiseCarrier" }]""", await CommandsAsync(client, 2));
    }

    [Theory]
    [InlineData("")]
    [InlineData("?lastReceived=abc")]
    [InlineData("?lastReceived=-2")]
    public async Task 前回受け取った番号がないか読めなければ400を返す(string query)
    {
        var client = await StartAsync();

        using var response = await client.GetAsync(new Uri($"/agv/AGV-1/commands{query}", UriKind.Relative), None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task 登録されていないAGVの通知には404を返す()
    {
        var client = await StartAsync();

        using var notification = await NotifyAsync(client, """{ "type": "battery", "percent": 50 }""", agv: "AGV-X");

        Assert.Equal(HttpStatusCode.NotFound, notification.StatusCode);
        Assert.Empty(_events.Published.OfType<AgvAutoRegistered>());
    }

    [Fact]
    public async Task 未登録のAGVが初めてGETしてきたら種類を決めないまま登録し切り離しのAGVと同じに答える()
    {
        var client = await StartAsync();

        using var commands = await client.GetAsync(new Uri("/agv/AGV-X/commands?lastReceived=-1", UriKind.Relative), None);
        using var again = await client.GetAsync(new Uri("/agv/AGV-X/commands?lastReceived=-1", UriKind.Relative), None);
        using var position = await client.GetAsync(new Uri("/agv/AGV-Y/position", UriKind.Relative), None);
        using var notification = await NotifyAsync(client, """{ "type": "battery", "percent": 50 }""", agv: "AGV-X");

        Assert.Equal(HttpStatusCode.OK, commands.StatusCode);
        Assert.Empty(JsonNode.Parse(await commands.Content.ReadAsStringAsync(None))!["commands"]!.AsArray());
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, position.StatusCode); // 位置の GET でも登録する。位置はわからない
        Assert.Equal(HttpStatusCode.NoContent, notification.StatusCode); // 通知は受けて無視する
        Assert.Equal([new AgvId("AGV-X"), new AgvId("AGV-Y")], _events.Published.OfType<AgvAutoRegistered>().Select(e => e.Agv)); // 知らせるのは1回だけ
        Assert.Equal([new AgvId("AGV-X"), new AgvId("AGV-Y")], _registrations.Added);
    }

    // ---- 位置を取りに来る ----

    [Fact]
    public async Task 最後に把握していた位置を返す()
    {
        var client = await StartAsync();
        await ReadyAtAsync("B", 180);

        using var response = await client.GetAsync(new Uri("/agv/AGV-1/position", UriKind.Relative), None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertJson("""{ "panel": "B", "heading": 180 }""", JsonNode.Parse(await response.Content.ReadAsStringAsync(None)));
    }

    [Fact]
    public async Task 位置がわからなければ204を返す()
    {
        var client = await StartAsync();

        using var response = await client.GetAsync(new Uri("/agv/AGV-1/position", UriKind.Relative), None);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ---- 通知 ----

    [Fact]
    public async Task 動作の受信と前進の完了の通知を受けて位置を更新する()
    {
        var client = await StartAsync();
        await ReadyAtAsync("A", 0);
        await _service.MoveAgvAsync(Agv, B, null, "test", None);
        await CommandsAsync(client, 1);

        using var received = await NotifyAsync(client, """{ "type": "motionReceived", "number": 2 }""");
        using var moved = await NotifyAsync(client, """{ "type": "moved", "number": 2, "direction": "forward", "panel": "B" }""");

        Assert.Equal(HttpStatusCode.NoContent, received.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, moved.StatusCode);
        Assert.Equal(new Pose("B", 0), _service.GetAgv(Agv).Position);
    }

    [Fact]
    public async Task キャリアの高さを測った通知で高さを保存する()
    {
        var client = await StartAsync();
        await ReadyAtAsync("T", 0);
        await _service.LowerCarrierAsync(Agv, "test", None);
        await CommandsAsync(client, 1);

        using var response = await NotifyAsync(client, """{ "type": "carrierHeightMeasured", "number": 2, "heightMm": 135 }""");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(135, _map.CarrierHeightOf(T));
    }

    [Fact]
    public async Task エラーの通知を受けて緊急停止中にする()
    {
        var client = await StartAsync();
        await ReadyAtAsync("A", 0);

        using var response = await NotifyAsync(client, """{ "type": "error", "kind": "bumperPressed" }""");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(AgvState.EmergencyStopped, _service.GetAgv(Agv).State);
    }

    [Fact]
    public async Task 位置がずれた可能性のあるエラーの通知を受けて異常にする()
    {
        var client = await StartAsync();
        await ReadyAtAsync("A", 0);

        using var response = await NotifyAsync(client, """{ "type": "error", "kind": "crossMissed" }""");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(AgvState.Error, _service.GetAgv(Agv).State);
    }

    [Fact]
    public async Task 充電残量の通知を受けて知らせる()
    {
        var client = await StartAsync();

        using var response = await NotifyAsync(client, """{ "type": "battery", "percent": 57 }""");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(57, Assert.Single(_events.Published.OfType<AgvBatteryChanged>()).Percent);
    }

    [Fact]
    public async Task アラームクリアと位置リセットの完了の通知を受ける()
    {
        var client = await StartAsync();
        await _service.ResetPositionAsync(Agv, new Pose("A", 0), None);
        await CommandsAsync(client, -1);

        using var reset = await NotifyAsync(client, """{ "type": "positionResetCompleted", "number": 1 }""");
        await _service.EmergencyStopAsync(Agv, None);
        await _service.ClearAlarmAsync(Agv, None);
        await CommandsAsync(client, 1);
        using var cleared = await NotifyAsync(client, """{ "type": "alarmCleared", "number": 3 }""");

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
        Assert.Equal(AgvState.Idle, _service.GetAgv(Agv).State);
    }

    [Fact]
    public async Task 回転の完了の通知を受ける()
    {
        var client = await StartAsync();
        await ReadyAtAsync("A", 0);

        using var response = await NotifyAsync(client, """{ "type": "rotated", "number": 9, "heading": 90 }""");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode); // 送っていない番号なので、受け付けずにログに残すだけ
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "type": "unknown", "number": 1 }""")]
    [InlineData("""{ "number": 1 }""")]
    [InlineData("""{ "type": "moved", "direction": "forward", "panel": "B" }""")]
    [InlineData("""{ "type": "moved", "number": 1, "direction": "sideways", "panel": "B" }""")]
    [InlineData("""{ "type": "moved", "number": 1, "direction": "forward" }""")]
    [InlineData("""{ "type": "rotated", "number": 1, "heading": 360 }""")]
    [InlineData("""{ "type": "carrierHeightMeasured", "number": 1 }""")]
    [InlineData("""{ "type": "error", "kind": "unknown" }""")]
    [InlineData("""{ "type": "battery", "percent": 101 }""")]
    [InlineData("""{ "type": "battery", "percent": "57" }""")]
    public async Task 形式が読めない通知には400を返す(string json)
    {
        var client = await StartAsync();
        await ReadyAtAsync("A", 0);

        using var response = await NotifyAsync(client, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
