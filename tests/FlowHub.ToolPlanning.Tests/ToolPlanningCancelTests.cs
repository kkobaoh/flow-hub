using FlowHub.BuildingBlocks;
using FlowHub.Consumables;
using FlowHub.Consumables.Application;
using FlowHub.Dispatching;
using FlowHub.Map;
using FlowHub.ToolPlanning.Application;
using FlowHub.Traffic;

namespace FlowHub.ToolPlanning.Tests;

/// <summary>
/// 割り当てたあとの搬送の取りやめ（取りやめの知らせを受けたときと、まるごと要らなくなった要求を取りやめること）のテスト。
/// </summary>
public class ToolPlanningCancelTests
{
    private static readonly DeviceId Device = new("D1");
    private static readonly StationId Station = new("S1");
    private static readonly ProcessTypeId Process = new("P");
    private static readonly CarrierTypeId Carrier = new("X");
    private static readonly VarietyId VarietyA = new("A");
    private static readonly VarietyId VarietyB = new("B");
    private static readonly VarietyId VarietyZ = new("Z");
    private static readonly AgvId Agv = new("AGV-1");
    private static readonly TransportId Held = new(900);
    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private sealed record Fixture(
        ToolPlanningService Planning,
        ConsumablesService Consumables,
        FakeDispatching Dispatching,
        RecordingReplenishment Replenishment,
        ManualTimeProvider Time);

    /// <summary>
    /// 装置 D1（ストッカー1つ、スロット3つ）と、ステーション S1。品種 A、B、Z は加工の種類 P（キャリアに2つ入る）。
    /// </summary>
    private static Fixture Setup(IEventPublisher? publisher = null)
    {
        var time = new ManualTimeProvider(T0);
        var dispatching = new FakeDispatching();
        var replenishment = new RecordingReplenishment();
        var consumables = new ConsumablesService(dispatching, new NoDevices(), new NoStations(), replenishment, publisher ?? new IgnoringPublisher(), time);
        consumables.RegisterDevice(new DeviceLayout(Device, new PanelId("D"), SlotsPerStocker: [3]));
        consumables.RegisterStation(new StationLayout(Station, new PanelId("S")));
        consumables.RegisterProcessType(new ProcessTypeSettings(Process, Carrier, CarrierCapacity: 2));
        consumables.RegisterVariety(VarietyA, Process);
        consumables.RegisterVariety(VarietyB, Process);
        consumables.RegisterVariety(VarietyZ, Process);
        return new Fixture(new ToolPlanningService(consumables, dispatching, time), consumables, dispatching, replenishment, time);
    }

    private static void AtStation(Fixture f, string id, VarietyId variety) =>
        f.Consumables.RegisterConsumable(new ConsumableId(id), variety, Process, ConsumableCondition.New, new AtStation(Station));

    private static void Carried(Fixture f, string id, VarietyId variety, ConsumableCondition condition = ConsumableCondition.New) =>
        f.Consumables.RegisterConsumable(new ConsumableId(id), variety, Process, condition, new OnAgv(Held));

    private static Task SetAsync(Fixture f, params (VarietyId Variety, int Count)[] counts) =>
        f.Consumables.SetStockerSettingAsync(Device, counts.ToDictionary(c => c.Variety, c => c.Count), None);

    private static Task CanceledAsync(Fixture f, TransportId transport, bool loaded) =>
        f.Planning.OnTransportCanceledAsync(new TransportCanceled(EventMetadata.Start("test", f.Time), transport, ConsumablesService.Requester, Agv, loaded), None);

    private static string[] CargoOf(AgvTransportOrder order) => order.CargoRefs.ToArray();

    // ---- 取りやめの知らせ ----

    [Fact]
    public async Task 何も積まずに取りやめたら予約を解いて要求を出し直す()
    {
        var f = Setup();
        AtStation(f, "A-1", VarietyA);
        await SetAsync(f, (VarietyA, 1));
        await f.Planning.ReplanAsync(None);
        f.Dispatching.AssignAll();
        var first = Assert.Single(f.Dispatching.Orders).Id;

        await CanceledAsync(f, first, loaded: false);

        Assert.Equal(2, f.Dispatching.Orders.Count);
        Assert.Equal(["A-1"], f.Dispatching.Orders[^1].Order.CargoRefs); // 予約を解いたので、また届けられる
    }

    [Fact]
    public async Task ほかの要求元の取りやめは扱わない()
    {
        var f = Setup();
        Carried(f, "A-1", VarietyA);

        await f.Planning.OnTransportCanceledAsync(new TransportCanceled(EventMetadata.Start("test", f.Time), Held, "work", Agv, Loaded: true), None);

        Assert.Empty(f.Dispatching.FromAgvOrders);
    }

    [Fact]
    public async Task 積んだまま取りやめたものが装置で必要ならAGVからその装置へ届ける()
    {
        var f = Setup();
        await SetAsync(f, (VarietyA, 1));
        await f.Planning.ReplanAsync(None); // 不足を見つけた時刻は T0
        f.Time.Now = T0.AddMinutes(5);
        Carried(f, "A-1", VarietyA);

        await CanceledAsync(f, Held, loaded: true);

        var order = Assert.Single(f.Dispatching.ActiveFromAgvOrders);
        Assert.Equal((Held, new PanelId("D"), "device:D1", T0, false), (order.LoadedBy, order.DestinationPanel, order.DestinationEndpoint, order.NeededBy, order.CargoRemains));
        Assert.Equal(["A-1"], CargoOf(order));
    }

    [Fact]
    public async Task AGVが持っているものをステーションの在庫より先に使う()
    {
        var f = Setup();
        AtStation(f, "A-2", VarietyA);
        Carried(f, "A-1", VarietyA);
        await SetAsync(f, (VarietyA, 1));

        await CanceledAsync(f, Held, loaded: true);

        Assert.Equal(["A-1"], CargoOf(Assert.Single(f.Dispatching.ActiveFromAgvOrders)));
        Assert.Empty(f.Dispatching.ActiveOrders); // ステーションからは届けない
    }

    [Fact]
    public async Task 一部だけ必要なら必要なものだけを届けて積んだままと伝える()
    {
        var f = Setup();
        Carried(f, "A-1", VarietyA);
        Carried(f, "B-1", VarietyB);
        await SetAsync(f, (VarietyA, 1));

        await CanceledAsync(f, Held, loaded: true);

        var order = Assert.Single(f.Dispatching.ActiveFromAgvOrders);
        Assert.Equal(["A-1"], CargoOf(order));
        Assert.True(order.CargoRemains);
    }

    [Fact]
    public async Task 複数の装置で必要なら不足の必要時刻が一番早い装置にだけ届ける()
    {
        var f = Setup();
        var device2 = new DeviceId("D2");
        f.Consumables.RegisterDevice(new DeviceLayout(device2, new PanelId("D2"), SlotsPerStocker: [3]));
        await f.Consumables.SetStockerSettingAsync(device2, new Dictionary<VarietyId, int> { [VarietyA] = 1 }, None);
        await f.Planning.ReplanAsync(None); // D2 の不足は T0
        f.Time.Now = T0.AddMinutes(5);
        await SetAsync(f, (VarietyA, 1));
        await f.Planning.ReplanAsync(None); // D1 の不足は 5分後
        Carried(f, "A-1", VarietyA);
        Carried(f, "A-2", VarietyA);

        await CanceledAsync(f, Held, loaded: true);

        var order = Assert.Single(f.Dispatching.ActiveFromAgvOrders);
        Assert.Equal(new PanelId("D2"), order.DestinationPanel);
        Assert.Equal(["A-1"], CargoOf(order));
        Assert.True(order.CargoRemains);
    }

    [Fact]
    public async Task どの装置でも要らなければすぐにステーションへ返す()
    {
        var f = Setup();
        Carried(f, "A-1", VarietyA);
        Carried(f, "Z-1", VarietyZ, ConsumableCondition.Used); // 回収の途中で取りやめた中古品
        f.Time.Now = T0.AddMinutes(3);

        await CanceledAsync(f, Held, loaded: true);

        var order = Assert.Single(f.Dispatching.ActiveFromAgvOrders);
        Assert.Equal((new PanelId("S"), "station:S1", T0.AddMinutes(3), false), (order.DestinationPanel, order.DestinationEndpoint, order.NeededBy, order.CargoRemains));
        Assert.Equal(["A-1", "Z-1"], CargoOf(order));
    }

    [Fact]
    public async Task 一部を届けたあとは残りをステーションへ返す()
    {
        var f = Setup();
        Carried(f, "A-1", VarietyA);
        Carried(f, "B-1", VarietyB);
        await SetAsync(f, (VarietyA, 1));
        await CanceledAsync(f, Held, loaded: true);
        var delivery = Assert.Single(f.Dispatching.FromAgvOrders).Id;

        await f.Planning.OnTransportCompletedAsync(new TransportCompleted(EventMetadata.Start("test", f.Time), delivery, ConsumablesService.Requester), None);
        await f.Planning.ReplanAsync(None);

        var back = f.Dispatching.ActiveFromAgvOrders.Last();
        Assert.Equal(new PanelId("S"), back.DestinationPanel);
        Assert.Equal(["B-1"], CargoOf(back)); // 届けたもの（届ける予約をしたもの）は返さない
    }

    [Fact]
    public async Task 全部を返し終えたらその搬送で積んだものはもう扱わない()
    {
        var f = Setup();
        Carried(f, "A-1", VarietyA);
        await CanceledAsync(f, Held, loaded: true);
        var back = Assert.Single(f.Dispatching.FromAgvOrders).Id;

        // 記録の上では、まだ AGV の中に残っている（受け渡しの完了の通知と食い違った）
        await f.Planning.OnTransportCompletedAsync(new TransportCompleted(EventMetadata.Start("test", f.Time), back, ConsumablesService.Requester), None);
        await f.Planning.ReplanAsync(None);

        Assert.Single(f.Dispatching.FromAgvOrders); // もう返すよう頼まない（配車は、その AGV を積んだままとは扱わないため）
    }

    // ---- まるごと要らなくなった割り当て済みの要求 ----

    [Fact]
    public async Task 割り当て済みの届ける要求がまるごと要らなくなったら取りやめる()
    {
        var f = Setup();
        AtStation(f, "A-1", VarietyA);
        await SetAsync(f, (VarietyA, 1));
        await f.Planning.ReplanAsync(None);
        f.Dispatching.AssignAll();
        var id = Assert.Single(f.Dispatching.Orders).Id;

        await SetAsync(f, (VarietyA, 0));
        await f.Planning.ReplanAsync(None);

        Assert.Equal([id], f.Dispatching.CancelRequests);
    }

    [Fact]
    public async Task 割り当て済みの届ける要求が一部だけ要らなくなっても取りやめない()
    {
        var f = Setup();
        AtStation(f, "A-1", VarietyA);
        AtStation(f, "A-2", VarietyA);
        await SetAsync(f, (VarietyA, 2));
        await f.Planning.ReplanAsync(None);
        f.Dispatching.AssignAll();

        await SetAsync(f, (VarietyA, 1));
        await f.Planning.ReplanAsync(None);

        Assert.Empty(f.Dispatching.CancelRequests);
    }

    [Fact]
    public async Task 割り当て済みの回収の要求がまるごと要らなくなったら取りやめる()
    {
        var f = Setup();
        f.Consumables.RegisterConsumable(new ConsumableId("Z-1"), VarietyZ, Process, ConsumableCondition.New, new InStocker(Device, new SlotRef(1, 1)));
        await SetAsync(f, (VarietyA, 0)); // Z は設定にないので回収する
        await f.Planning.ReplanAsync(None);
        f.Dispatching.AssignAll();
        var id = Assert.Single(f.Dispatching.Orders).Id;

        await SetAsync(f, (VarietyZ, 1));
        await f.Planning.ReplanAsync(None);

        Assert.Equal([id], f.Dispatching.CancelRequests);
    }

    [Fact]
    public async Task 積み込んだあとの回収の要求は取りやめない()
    {
        // 消耗品の管理が、配車の「搬送の許可を出してほしい」を受けて、受け渡しの完了で所在を AGV の中にする
        var subscriptions = new EventSubscriptions();
        var bus = new InProcessEventBus(subscriptions);
        var f = Setup(bus);
        ConsumablesSubscriptions.Register(subscriptions, f.Consumables);
        f.Consumables.RegisterConsumable(new ConsumableId("Z-1"), VarietyZ, Process, ConsumableCondition.New, new InStocker(Device, new SlotRef(1, 1)));
        await SetAsync(f, (VarietyA, 0));
        await f.Planning.ReplanAsync(None);
        f.Dispatching.AssignAll();
        var id = Assert.Single(f.Dispatching.Orders).Id;
        await bus.PublishAsync(new TransferPermissionRequested(EventMetadata.Start("test", f.Time), id, ConsumablesService.Requester, ["Z-1"], "device:D1", TransferSide.Load), None);
        await bus.FlushAsync(None);
        await f.Consumables.ReportTransferCompletedAsync(id, [new ConsumableId("Z-1")], None);

        await SetAsync(f, (VarietyZ, 1));
        await f.Planning.ReplanAsync(None);

        Assert.Empty(f.Dispatching.CancelRequests);
    }
}
