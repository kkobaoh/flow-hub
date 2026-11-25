using FlowHub.BuildingBlocks;
using FlowHub.Consumables;
using FlowHub.Consumables.Application;
using FlowHub.Dispatching;
using FlowHub.Map;
using FlowHub.ToolPlanning.Application;

namespace FlowHub.ToolPlanning.Tests;

public class ToolPlanningServiceTests
{
    private static readonly DeviceId Device = new("D1");
    private static readonly StationId Station = new("S1");
    private static readonly ProcessTypeId Process = new("P");
    private static readonly CarrierTypeId Carrier = new("X");
    private static readonly VarietyId VarietyA = new("A");
    private static readonly VarietyId VarietyB = new("B");
    private static readonly VarietyId VarietyZ = new("Z");
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddMinutes(5);
    private static readonly CancellationToken None = CancellationToken.None;

    private sealed record Fixture(
        ToolPlanningService Planning,
        ConsumablesService Consumables,
        FakeDispatching Dispatching,
        RecordingReplenishment Replenishment,
        NoStations Stations,
        ManualTimeProvider Time);

    /// <summary>
    /// 装置 D1（ストッカー1つ、スロット3つ）と、ステーション S1。品種 A、B、Z は加工の種類 P（キャリアの種類 X）。
    /// </summary>
    private static Fixture Setup(int carrierCapacity = 2)
    {
        var time = new ManualTimeProvider(T0);
        var dispatching = new FakeDispatching();
        var replenishment = new RecordingReplenishment();
        var stations = new NoStations();
        var consumables = new ConsumablesService(dispatching, new NoDevices(), stations, replenishment, new IgnoringPublisher(), time);
        consumables.RegisterDevice(new DeviceLayout(Device, new PanelId("D"), SlotsPerStocker: [3]));
        consumables.RegisterStation(new StationLayout(Station, new PanelId("S")));
        consumables.RegisterProcessType(new ProcessTypeSettings(Process, Carrier, carrierCapacity));
        consumables.RegisterVariety(VarietyA, Process);
        consumables.RegisterVariety(VarietyB, Process);
        consumables.RegisterVariety(VarietyZ, Process);
        var planning = new ToolPlanningService(consumables, dispatching, time);
        return new Fixture(planning, consumables, dispatching, replenishment, stations, time);
    }

    private static void AtStation(Fixture f, string id, VarietyId variety) =>
        f.Consumables.RegisterConsumable(new ConsumableId(id), variety, Process, ConsumableCondition.New, new AtStation(Station));

    private static void InStocker(Fixture f, string id, VarietyId variety, int slot, ConsumableCondition condition = ConsumableCondition.New) =>
        f.Consumables.RegisterConsumable(new ConsumableId(id), variety, Process, condition, new InStocker(Device, new SlotRef(1, slot)));

    private static Task<StockerSettingResult> SetAsync(Fixture f, params (VarietyId Variety, int Count)[] counts) =>
        f.Consumables.SetStockerSettingAsync(Device, counts.ToDictionary(c => c.Variety, c => c.Count), None);

    /// <summary>取りやめずに残っている要求の荷（頼んだ順）。例："A-1,A-2"。</summary>
    private static string[] ActiveCargo(Fixture f) =>
        f.Dispatching.ActiveOrders.Select(o => string.Join(",", o.CargoRefs)).ToArray();

    // ---- 頼んだ搬送の中身（画面の搬送の一覧に出す） ----

    [Fact]
    public async Task 頼んだ搬送ごとに届けるか回収するかと品種ごとの数を出す()
    {
        var f = Setup();
        AtStation(f, "A-1", VarietyA);
        AtStation(f, "A-2", VarietyA);
        InStocker(f, "Z-1", VarietyZ, 1); // 設定にない品種
        await SetAsync(f, (VarietyA, 2));
        await f.Planning.ReplanAsync(None);
        var delivery = f.Dispatching.Orders.Single(o => o.Order.CargoRefs.Contains("A-1")).Id;
        var recovery = f.Dispatching.Orders.Single(o => o.Order.CargoRefs.Contains("Z-1")).Id;

        var descriptions = f.Planning.DescribeTransports();

        Assert.Equal(2, descriptions.Count);
        Assert.Equal(new TransportDescription(TransportPurpose.Delivery, [new CargoCount(VarietyA, 2)], [new ConsumableId("A-1"), new ConsumableId("A-2")]), descriptions[delivery]);
        Assert.Equal(new TransportDescription(TransportPurpose.Recovery, [new CargoCount(VarietyZ, 1)], [new ConsumableId("Z-1")]), descriptions[recovery]);
    }

    [Fact]
    public async Task 品種のわからない消耗品は品種なしで数え終わった搬送は出さない()
    {
        var f = Setup();
        InStocker(f, "A-1", VarietyA, 1);
        await SetAsync(f, (VarietyA, 1));
        await f.Consumables.ReportIdMismatchAsync(Device, new SlotRef(1, 1), new ConsumableId("X-9"), None); // 未登録の ID は品種がわからない
        await f.Planning.ReplanAsync(None);
        var recovery = f.Dispatching.Orders.Single(o => o.Order.CargoRefs.Contains("X-9")).Id;

        Assert.Equal(new TransportDescription(TransportPurpose.Recovery, [new CargoCount(null, 1)], [new ConsumableId("X-9")]), f.Planning.DescribeTransports()[recovery]);

        await f.Planning.OnTransportCompletedAsync(new TransportCompleted(EventMetadata.Start("test", f.Time), recovery, ConsumablesService.Requester), None);
        Assert.False(f.Planning.DescribeTransports().ContainsKey(recovery));
    }

    // ---- 運転状態 ----

    [Fact]
    public async Task 停止中は不足と回収を判断せず補充も頼まない()
    {
        var f = Setup();
        AtStation(f, "A-1", VarietyA);
        await SetAsync(f, (VarietyA, 2));
        f.Dispatching.IsRunning = false;

        await f.Planning.ReplanAsync(None);

        Assert.Empty(f.Dispatching.Orders);
        Assert.Empty(f.Replenishment.Requests);
    }

    // ---- 届ける ----

    [Fact]
    public async Task 不足をステーションから装置へ届ける要求を出す()
    {
        var f = Setup();
        AtStation(f, "A-1", VarietyA);
        AtStation(f, "A-2", VarietyA);
        await SetAsync(f, (VarietyA, 2));

        await f.Planning.ReplanAsync(None);

        var order = Assert.Single(f.Dispatching.Orders).Order;
        Assert.Equal(ConsumablesService.Requester, order.Requester);
        Assert.Equal(["A-1", "A-2"], order.CargoRefs);
        Assert.Equal((new PanelId("S"), "station:S1"), (order.SourcePanel, order.SourceEndpoint));
        Assert.Equal((new PanelId("D"), "device:D1"), (order.DestinationPanel, order.DestinationEndpoint));
        Assert.Equal(Carrier, order.Carrier);
        Assert.Equal(T0, order.NeededBy);
    }

    [Fact]
    public async Task キャリアに入る個数ごとに往復を分ける()
    {
        var f = Setup();
        AtStation(f, "A-1", VarietyA);
        AtStation(f, "A-2", VarietyA);
        AtStation(f, "A-3", VarietyA);
        await SetAsync(f, (VarietyA, 3));

        await f.Planning.ReplanAsync(None);

        Assert.Equal(["A-1,A-2", "A-3"], ActiveCargo(f));
    }

    [Fact]
    public async Task 不足を見つけた時刻の順に積み同じなら品種のIDの順に積む()
    {
        // B は T0 に不足（ステーションになく補充待ち）。T1 に B が入庫し、A と Z の設定が加わる
        var f = Setup(carrierCapacity: 1);
        AtStation(f, "A-1", VarietyA);
        AtStation(f, "Z-1", VarietyZ);
        await SetAsync(f, (VarietyB, 1));
        await f.Planning.ReplanAsync(None);

        f.Time.Now = T1;
        f.Consumables.RegisterExpectedArrival(new ConsumableId("B-1"), VarietyB);
        await f.Consumables.ReportReadAtStationAsync(Station, new ConsumableId("B-1"), None);
        await SetAsync(f, (VarietyZ, 1), (VarietyA, 1), (VarietyB, 1));
        await f.Planning.ReplanAsync(None);

        Assert.Equal(["B-1", "A-1", "Z-1"], ActiveCargo(f));
        Assert.Equal([T0, T1, T1], f.Dispatching.ActiveOrders.Select(o => o.NeededBy));
    }

    [Fact]
    public async Task まとめた要求の必要時刻は一番早い時刻にする()
    {
        // A は T0 から不足が続き、B は T1 に不足
        var f = Setup();
        await SetAsync(f, (VarietyA, 1));
        await f.Planning.ReplanAsync(None);

        f.Time.Now = T1;
        f.Consumables.RegisterExpectedArrival(new ConsumableId("A-1"), VarietyA);
        await f.Consumables.ReportReadAtStationAsync(Station, new ConsumableId("A-1"), None);
        AtStation(f, "B-1", VarietyB);
        await SetAsync(f, (VarietyA, 1), (VarietyB, 1));
        await f.Planning.ReplanAsync(None);

        var order = Assert.Single(f.Dispatching.ActiveOrders);
        Assert.Equal(["A-1", "B-1"], order.CargoRefs);
        Assert.Equal(T0, order.NeededBy);
    }

    [Fact]
    public async Task 空いているスロットの数までしか届けない()
    {
        // スロット3つのうち、中古品が2つふさいでいる
        var f = Setup();
        InStocker(f, "U-1", VarietyA, 1, ConsumableCondition.Used);
        InStocker(f, "U-2", VarietyA, 2, ConsumableCondition.Used);
        AtStation(f, "A-1", VarietyA);
        AtStation(f, "A-2", VarietyA);
        await SetAsync(f, (VarietyA, 2));

        await f.Planning.ReplanAsync(None);

        Assert.Equal(["A-1"], ActiveCargo(f));
    }

    [Fact]
    public async Task ステーションに一部しかなければあるぶんを届けて残りの補充を頼む()
    {
        var f = Setup();
        AtStation(f, "A-1", VarietyA);
        await SetAsync(f, (VarietyA, 3));

        await f.Planning.ReplanAsync(None);

        Assert.Equal(["A-1"], ActiveCargo(f));
        Assert.Equal([(VarietyA, 2)], f.Replenishment.Requests);
    }

    [Fact]
    public async Task 割り当てた要求は届ける途中として数え同じ不足に重ねて頼まない()
    {
        var f = Setup();
        AtStation(f, "A-1", VarietyA);
        AtStation(f, "A-2", VarietyA);
        await SetAsync(f, (VarietyA, 1));
        await f.Planning.ReplanAsync(None);
        f.Dispatching.AssignAll();

        await f.Planning.ReplanAsync(None);

        Assert.Single(f.Dispatching.Orders);
        Assert.Empty(f.Dispatching.Cancelled);
    }

    [Fact]
    public async Task 割り当て前の要求はやり直しのときに取りやめて作り直す()
    {
        var f = Setup();
        AtStation(f, "A-1", VarietyA);
        await SetAsync(f, (VarietyA, 1));
        await f.Planning.ReplanAsync(None);

        await f.Planning.ReplanAsync(None);

        Assert.Equal([new TransportId(1)], f.Dispatching.Cancelled);
        Assert.Equal(["A-1"], ActiveCargo(f));
        Assert.Equal(T0, f.Dispatching.ActiveOrders.Single().NeededBy);
    }

    [Fact]
    public async Task 要らなくなった割り当て前の要求は取りやめて予約を解く()
    {
        var f = Setup();
        AtStation(f, "A-1", VarietyA);
        await SetAsync(f, (VarietyA, 1));
        await f.Planning.ReplanAsync(None);

        await SetAsync(f, (VarietyA, 0));
        await f.Planning.ReplanAsync(None);

        Assert.Empty(ActiveCargo(f));
        Assert.Equal([new ConsumableId("A-1")], f.Consumables.GetStationStock(VarietyA).Select(s => s.Consumable));
    }

    [Fact]
    public async Task 完了した搬送はもう取りやめようとしない()
    {
        var f = Setup();
        AtStation(f, "A-1", VarietyA);
        await SetAsync(f, (VarietyA, 1));
        await f.Planning.ReplanAsync(None);
        f.Dispatching.AssignAll();

        await f.Planning.OnTransportCompletedAsync(new TransportCompleted(Meta(), new TransportId(1), ConsumablesService.Requester), None);
        await f.Planning.ReplanAsync(None);

        Assert.Empty(f.Dispatching.Cancelled);
        Assert.Equal(0, f.Dispatching.CancelCalls);
    }

    [Fact]
    public async Task ほかの要求元の搬送の完了は扱わない()
    {
        var f = Setup();
        AtStation(f, "A-1", VarietyA);
        await SetAsync(f, (VarietyA, 1));
        await f.Planning.ReplanAsync(None);

        await f.Planning.OnTransportCompletedAsync(new TransportCompleted(Meta(), new TransportId(1), "work"), None);
        await f.Planning.ReplanAsync(None);

        Assert.Equal([new TransportId(1)], f.Dispatching.Cancelled);
    }

    // ---- 回収する ----

    [Fact]
    public async Task 設定にない品種の新品を装置からステーションへ回収する要求を出す()
    {
        var f = Setup();
        InStocker(f, "Z-1", VarietyZ, 1);
        await SetAsync(f, (VarietyA, 0));

        await f.Planning.ReplanAsync(None);

        var order = Assert.Single(f.Dispatching.Orders).Order;
        Assert.Equal(["Z-1"], order.CargoRefs);
        Assert.Equal((new PanelId("D"), "device:D1"), (order.SourcePanel, order.SourceEndpoint));
        Assert.Equal((new PanelId("S"), "station:S1"), (order.DestinationPanel, order.DestinationEndpoint));
        Assert.Equal(T0, order.NeededBy);
    }

    [Fact]
    public async Task 回収は設定にない品種を廃棄より先に積む()
    {
        // ID の順なら 0-U が先だが、理由の順で Z-1 を先に積む
        var f = Setup();
        InStocker(f, "Z-1", VarietyZ, 1);
        InStocker(f, "0-U", VarietyA, 2, ConsumableCondition.Used);
        await f.Consumables.RequestDiscardAsync(Device, new ConsumableId("0-U"), None);
        await SetAsync(f, (VarietyA, 0));

        await f.Planning.ReplanAsync(None);

        Assert.Equal(["Z-1,0-U"], ActiveCargo(f));
    }

    [Fact]
    public async Task 回収は設定より多い品種を廃棄より先に積む()
    {
        // スロット3つが A×2（設定は1）と廃棄の中古品でふさがり、B の不足を補う空きがないので、A の1つを回収する
        var f = Setup();
        InStocker(f, "A-8", VarietyA, 1);
        InStocker(f, "A-9", VarietyA, 2);
        InStocker(f, "0-U", VarietyA, 3, ConsumableCondition.Used);
        await f.Consumables.RequestDiscardAsync(Device, new ConsumableId("0-U"), None);
        await SetAsync(f, (VarietyA, 1), (VarietyB, 1));

        await f.Planning.ReplanAsync(None);

        Assert.Equal(["A-8,0-U"], ActiveCargo(f));
    }

    [Fact]
    public async Task 不整合の消耗品はほかの回収より先に積む()
    {
        // スロット1の照合が合わず、読み取ったのは Z-9。ID の順なら Z-1 が先だが、不整合の Z-9 を先に積む
        var f = Setup();
        InStocker(f, "A-1", VarietyA, 1);
        InStocker(f, "Z-1", VarietyZ, 2);
        AtStation(f, "Z-9", VarietyZ);
        await SetAsync(f, (VarietyA, 0));
        await f.Consumables.ReportIdMismatchAsync(Device, new SlotRef(1, 1), new ConsumableId("Z-9"), None);

        await f.Planning.ReplanAsync(None);

        Assert.Equal(["Z-9,Z-1"], ActiveCargo(f));
    }

    [Fact]
    public async Task 品種がわからない不整合の消耗品も加工の種類のキャリアで回収する()
    {
        var f = Setup();
        InStocker(f, "A-1", VarietyA, 1);
        await f.Consumables.ReportIdMismatchAsync(Device, new SlotRef(1, 1), new ConsumableId("X-1"), None);

        await f.Planning.ReplanAsync(None);

        var order = Assert.Single(f.Dispatching.ActiveOrders);
        Assert.Equal(["X-1"], order.CargoRefs);
        Assert.Equal(Carrier, order.Carrier);
    }

    [Fact]
    public async Task 届けるものと回収するものは別々の往復にする()
    {
        var f = Setup();
        InStocker(f, "Z-1", VarietyZ, 1);
        AtStation(f, "A-1", VarietyA);
        await SetAsync(f, (VarietyA, 1));

        await f.Planning.ReplanAsync(None);

        Assert.Equal(["A-1", "Z-1"], ActiveCargo(f));
    }

    [Fact]
    public async Task 回収の必要時刻は回収が必要だと判断した時刻で続くあいだ保つ()
    {
        var f = Setup();
        InStocker(f, "Z-1", VarietyZ, 1);
        await SetAsync(f, (VarietyA, 0));
        await f.Planning.ReplanAsync(None);

        f.Time.Now = T1;
        await f.Planning.ReplanAsync(None);

        Assert.Equal([T0], f.Dispatching.ActiveOrders.Select(o => o.NeededBy));
    }

    // ---- 届ける消耗品の指名 ----

    [Fact]
    public async Task 指名があれば先入れ先出しより先に指名された消耗品を届ける()
    {
        var f = Setup();
        AtStation(f, "A-1", VarietyA);
        AtStation(f, "A-2", VarietyA);
        await SetAsync(f, (VarietyA, 1));

        await f.Planning.DesignateAsync(Device, new ConsumableId("A-2"), None);

        Assert.Equal(["A-2"], ActiveCargo(f));
    }

    [Fact]
    public async Task 指名した品種が不足していなければ不足が出るまで待つ()
    {
        var f = Setup();
        AtStation(f, "A-1", VarietyA);
        AtStation(f, "A-2", VarietyA);
        await SetAsync(f, (VarietyA, 0));
        await f.Planning.DesignateAsync(Device, new ConsumableId("A-2"), None);

        Assert.Empty(ActiveCargo(f));

        await SetAsync(f, (VarietyA, 1));
        await f.Planning.ReplanAsync(None);

        Assert.Equal(["A-2"], ActiveCargo(f));
    }

    [Fact]
    public async Task 割り当て前なら指名を取り消せて先入れ先出しに戻る()
    {
        var f = Setup();
        AtStation(f, "A-1", VarietyA);
        AtStation(f, "A-2", VarietyA);
        await SetAsync(f, (VarietyA, 1));
        Assert.Equal(DesignationResult.Designated, await f.Planning.DesignateAsync(Device, new ConsumableId("A-2"), None));

        var cancelled = await f.Planning.CancelDesignationAsync(new ConsumableId("A-2"), None);

        Assert.Equal(DesignationCancelResult.Canceled, cancelled);
        Assert.Equal(["A-1"], ActiveCargo(f));
    }

    [Fact]
    public async Task 割り当て済みなら指名は取り消せない()
    {
        var f = Setup();
        AtStation(f, "A-1", VarietyA);
        AtStation(f, "A-2", VarietyA);
        await SetAsync(f, (VarietyA, 1));
        await f.Planning.DesignateAsync(Device, new ConsumableId("A-2"), None);
        f.Dispatching.AssignAll();

        var cancelled = await f.Planning.CancelDesignationAsync(new ConsumableId("A-2"), None);

        Assert.Equal(DesignationCancelResult.InAssignedTransport, cancelled);
        Assert.Equal(["A-2"], ActiveCargo(f));
    }

    // ---- 在庫の手入力 ----

    [Fact]
    public async Task 割り当て前の要求に入っている消耗品を直すと要求を取りやめて直し残りを出し直す()
    {
        var f = Setup();
        AtStation(f, "A-1", VarietyA);
        AtStation(f, "A-2", VarietyA);
        await SetAsync(f, (VarietyA, 2));
        await f.Planning.ReplanAsync(None);

        var result = await f.Planning.EditManuallyAsync(new ConsumableId("A-1"), VarietyB, ConsumableCondition.New, new AtStation(Station), None);

        Assert.Equal(ManualEntryResult.Saved, result);
        Assert.Equal(VarietyB, f.Consumables.GetConsumable(new ConsumableId("A-1")).Variety);
        Assert.Equal(["A-2"], ActiveCargo(f));
    }

    [Fact]
    public async Task 割り当て済みの要求に入っている消耗品は直すも消すも断る()
    {
        var f = Setup();
        AtStation(f, "A-1", VarietyA);
        await SetAsync(f, (VarietyA, 1));
        await f.Planning.ReplanAsync(None);
        f.Dispatching.AssignAll();

        Assert.Equal(ManualEntryResult.InAssignedTransport, await f.Planning.EditManuallyAsync(new ConsumableId("A-1"), VarietyB, ConsumableCondition.New, new AtStation(Station), None));
        Assert.Equal(ManualEntryResult.InAssignedTransport, await f.Planning.DeleteManuallyAsync(new ConsumableId("A-1"), None));
        Assert.Equal(VarietyA, f.Consumables.GetConsumable(new ConsumableId("A-1")).Variety);
        Assert.Equal(["A-1"], ActiveCargo(f));
    }

    [Fact]
    public async Task 割り当て前の要求に入っている消耗品を消すと要求を取りやめて消す()
    {
        var f = Setup();
        AtStation(f, "A-1", VarietyA);
        await SetAsync(f, (VarietyA, 1));
        await f.Planning.ReplanAsync(None);

        Assert.Equal(ManualEntryResult.Saved, await f.Planning.DeleteManuallyAsync(new ConsumableId("A-1"), None));

        Assert.False(f.Consumables.TryGetConsumable(new ConsumableId("A-1"), out _));
        Assert.Empty(ActiveCargo(f));
    }

    // ---- 上位システムの出庫の指示 ----

    [Fact]
    public async Task 予約のない消耗品の出庫の指示はそのまま出す()
    {
        var f = Setup();
        AtStation(f, "A-1", VarietyA);

        var result = await f.Planning.RequestShipmentAsync(new ConsumableId("A-1"), None);

        Assert.Equal(ShipmentResult.Requested, result);
        Assert.Equal([new ConsumableId("A-1")], f.Stations.Shipments);
    }

    [Fact]
    public async Task 割り当て前の要求に入っている消耗品の出庫の指示は要求を取りやめて出し残りを出し直す()
    {
        var f = Setup();
        AtStation(f, "A-1", VarietyA);
        AtStation(f, "A-2", VarietyA);
        await SetAsync(f, (VarietyA, 2));
        await f.Planning.ReplanAsync(None);

        var result = await f.Planning.RequestShipmentAsync(new ConsumableId("A-1"), None);

        Assert.Equal(ShipmentResult.Requested, result);
        Assert.Equal([new ConsumableId("A-1")], f.Stations.Shipments);
        Assert.Equal(["A-2"], ActiveCargo(f));
    }

    [Fact]
    public async Task 割り当て済みの要求に入っている消耗品の出庫の指示は受け付けない()
    {
        var f = Setup();
        AtStation(f, "A-1", VarietyA);
        await SetAsync(f, (VarietyA, 1));
        await f.Planning.ReplanAsync(None);
        f.Dispatching.AssignAll();

        var result = await f.Planning.RequestShipmentAsync(new ConsumableId("A-1"), None);

        Assert.Equal(ShipmentResult.InAssignedTransport, result);
        Assert.Empty(f.Stations.Shipments);
        Assert.Equal(["A-1"], ActiveCargo(f));
    }

    // ---- ステーション ----

    [Fact]
    public async Task ステーションが2台以上あるときはエラーにする()
    {
        var f = Setup();
        f.Consumables.RegisterStation(new StationLayout(new StationId("S2"), new PanelId("S2")));
        AtStation(f, "A-1", VarietyA);
        await SetAsync(f, (VarietyA, 1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Planning.ReplanAsync(None));
    }

    private static EventMetadata Meta() => EventMetadata.Start("test", new ManualTimeProvider(T0));
}
