using FlowHub.BuildingBlocks;
using FlowHub.Consumables.Application;
using FlowHub.Consumables.Domain;
using FlowHub.Dispatching;
using FlowHub.Map;

namespace FlowHub.Consumables.Tests;

/// <summary>
/// 装置からの通知（加工部への取り込み、ストッカーに戻した、照合のエラー、交換の完了、段取り替えのリクエスト）のテスト。
/// </summary>
public class ConsumablesDeviceNotificationsTests
{
    private static readonly DeviceId Device = new("D1");
    private static readonly StationId Station = new("S1");
    private static readonly ProcessTypeId Process = new("P");
    private static readonly ProcessTypeId OtherProcess = new("Q");
    private static readonly VarietyId VarietyA = new("A");
    private static readonly VarietyId VarietyQ = new("QA");
    private static readonly TransportId Transport1 = new(1);
    private static readonly CancellationToken None = CancellationToken.None;

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
    }

    private sealed class NoDispatching : IDispatchingService
    {
        public Task<TransportId> RequestTransportAsync(TransportOrder order, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> CancelTransportAsync(TransportId id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ReportTransferCompletedAsync(TransportId id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingDevices : IDeviceGateway
    {
        public List<IReadOnlyList<CargoSlot>> Grants { get; } = [];

        public Task GrantTransferAsync(DeviceId device, TransportId transport, TransferSide side, IReadOnlyList<CargoSlot> cargo, CancellationToken cancellationToken)
        {
            Grants.Add(cargo);
            return Task.CompletedTask;
        }
    }

    private sealed class NoStations : IStationGateway
    {
        public Task GrantTransferAsync(StationId station, TransportId transport, TransferSide side, IReadOnlyList<ConsumableId> cargo, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class RecordingUpstream : IUpstreamGateway
    {
        public List<(DeviceId Device, VarietyId Variety)> SetupErrors { get; } = [];

        public Task RequestReplenishmentAsync(VarietyId variety, int quantity, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task NotifySetupErrorAsync(DeviceId device, VarietyId variety, CancellationToken cancellationToken)
        {
            SetupErrors.Add((device, variety));
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

    private sealed record Fixture(ConsumablesService Service, RecordingDevices Devices, RecordingUpstream Upstream, RecordingPublisher Publisher);

    /// <summary>
    /// 装置 D1（ストッカー1つ、スロット3つ）と、ステーション S1。品種 A は加工の種類 P、品種 QA は加工の種類 Q。
    /// </summary>
    private static Fixture Setup()
    {
        var devices = new RecordingDevices();
        var upstream = new RecordingUpstream();
        var publisher = new RecordingPublisher();
        var service = new ConsumablesService(new NoDispatching(), devices, new NoStations(), upstream, publisher, new FixedTimeProvider());
        service.RegisterDevice(new DeviceLayout(Device, new PanelId("D"), SlotsPerStocker: [3]));
        service.RegisterStation(new StationLayout(Station, new PanelId("S")));
        service.RegisterProcessType(new ProcessTypeSettings(Process, new CarrierTypeId("X"), CarrierCapacity: 2));
        service.RegisterProcessType(new ProcessTypeSettings(OtherProcess, new CarrierTypeId("Y"), CarrierCapacity: 2));
        service.RegisterVariety(VarietyA, Process);
        service.RegisterVariety(VarietyQ, OtherProcess);
        return new Fixture(service, devices, upstream, publisher);
    }

    private static ConsumableId InStocker(Fixture f, string id, int slot, ConsumableCondition condition = ConsumableCondition.New)
    {
        var consumable = new ConsumableId(id);
        f.Service.RegisterConsumable(consumable, VarietyA, Process, condition, new InStocker(Device, new SlotRef(1, slot)));
        return consumable;
    }

    private static ConsumableId AtStation(Fixture f, string id)
    {
        var consumable = new ConsumableId(id);
        f.Service.RegisterConsumable(consumable, VarietyA, Process, ConsumableCondition.New, new AtStation(Station));
        return consumable;
    }

    private static TransferPermissionRequested Permission(TransportId transport, string endpoint, TransferSide side, params ConsumableId[] cargo) =>
        new(EventMetadata.Start("test", new FixedTimeProvider()), transport, ConsumablesService.Requester, cargo.Select(c => c.Value).ToArray(), endpoint, side);

    private static SlotRef Slot(int slot) => new(1, slot);

    // ---- 加工部への取り込み ----

    [Fact]
    public async Task 取り込んだ消耗品は加工部の中古品になり元のスロットを覚える()
    {
        var f = Setup();
        var c1 = InStocker(f, "C-1", 1);

        var report = await f.Service.ReportTakenIntoProcessingAsync(Device, c1, None);

        var view = f.Service.GetConsumable(c1);
        Assert.Equal(new InProcessing(Device, Slot(1)), view.Location);
        Assert.Equal(ConsumableCondition.Used, view.Condition);
        Assert.Empty(report.Mismatched);
    }

    [Fact]
    public async Task 取り込み中の元のスロットはふだんは入れ先にしない()
    {
        var f = Setup();
        var c1 = InStocker(f, "C-1", 1);
        await f.Service.ReportTakenIntoProcessingAsync(Device, c1, None);
        var n = AtStation(f, "N-1");

        await f.Service.OnTransferPermissionRequestedAsync(Permission(Transport1, Endpoints.Device(Device), TransferSide.Unload, n), None);

        Assert.Equal([new CargoSlot(n, Slot(2))], Assert.Single(f.Devices.Grants));
    }

    [Fact]
    public async Task 空きスロットが足りなければ取り込み中の元のスロットも入れ先に使う()
    {
        var f = Setup();
        var c1 = InStocker(f, "C-1", 1);
        InStocker(f, "C-2", 2);
        InStocker(f, "C-3", 3);
        await f.Service.ReportTakenIntoProcessingAsync(Device, c1, None);
        var n = AtStation(f, "N-1");

        await f.Service.OnTransferPermissionRequestedAsync(Permission(Transport1, Endpoints.Device(Device), TransferSide.Unload, n), None);

        Assert.Equal([new CargoSlot(n, Slot(1))], Assert.Single(f.Devices.Grants));
    }

    [Fact]
    public async Task 取り込み中の元のスロットは届ける数の上限では空きとして数える()
    {
        var f = Setup();
        var c1 = InStocker(f, "C-1", 1);
        InStocker(f, "C-2", 2);
        InStocker(f, "C-3", 3);

        await f.Service.ReportTakenIntoProcessingAsync(Device, c1, None);

        Assert.Equal(1, f.Service.EvaluateDevice(Device).FreeSlots);
    }

    [Fact]
    public async Task 記録上ストッカーにない消耗品の取り込みは通知を正として加工部にし食い違いを返す()
    {
        var f = Setup();
        var a = AtStation(f, "A-1");

        var report = await f.Service.ReportTakenIntoProcessingAsync(Device, a, None);

        Assert.Equal(new InProcessing(Device, null), f.Service.GetConsumable(a).Location);
        Assert.Equal([a], report.Mismatched);
    }

    [Fact]
    public async Task 未登録の消耗品の取り込みはエラーにする()
    {
        var f = Setup();

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ReportTakenIntoProcessingAsync(Device, new ConsumableId("X-1"), None));
    }

    // ---- ストッカーに戻した ----

    [Fact]
    public async Task 戻した中古品は通知されたスロットのストッカーにある()
    {
        var f = Setup();
        var c1 = InStocker(f, "C-1", 1);
        await f.Service.ReportTakenIntoProcessingAsync(Device, c1, None);

        var report = await f.Service.ReportReturnedToStockerAsync(Device, c1, Slot(2), None);

        var view = f.Service.GetConsumable(c1);
        Assert.Equal(new InStocker(Device, Slot(2)), view.Location);
        Assert.Equal(ConsumableCondition.Used, view.Condition);
        Assert.Empty(report.Mismatched);
    }

    [Fact]
    public async Task 戻したスロットに記録上別の消耗品があればその消耗品を所在不明にして食い違いを返す()
    {
        var f = Setup();
        var c1 = InStocker(f, "C-1", 1);
        var c2 = InStocker(f, "C-2", 2);
        await f.Service.ReportTakenIntoProcessingAsync(Device, c1, None);

        var report = await f.Service.ReportReturnedToStockerAsync(Device, c1, Slot(2), None);

        Assert.Equal(new UnknownLocation(), f.Service.GetConsumable(c2).Location);
        Assert.Equal([c2], report.Mismatched);
    }

    [Fact]
    public async Task 記録上加工部にない消耗品が戻ったら通知を正として所在を更新し食い違いを返す()
    {
        var f = Setup();
        var a = AtStation(f, "A-1");

        var report = await f.Service.ReportReturnedToStockerAsync(Device, a, Slot(3), None);

        Assert.Equal(new InStocker(Device, Slot(3)), f.Service.GetConsumable(a).Location);
        Assert.Equal([a], report.Mismatched);
    }

    // ---- 照合のエラー ----

    [Fact]
    public async Task 照合のエラーで記録上の消耗品と読み取った消耗品を所在不明にし読み取ったものはスロットにあるとする()
    {
        var f = Setup();
        var c1 = InStocker(f, "C-1", 1);
        var c5 = AtStation(f, "C-5");

        await f.Service.ReportIdMismatchAsync(Device, Slot(1), c5, None);

        Assert.Equal(new UnknownLocation(), f.Service.GetConsumable(c1).Location);
        var read = f.Service.GetConsumable(c5);
        Assert.Equal(new UnknownLocation(Device, Slot(1)), read.Location);
        Assert.True(read.NeedsCheck);
    }

    [Fact]
    public async Task 照合のエラーのスロットはふさがっているものとして数える()
    {
        // スロット1は、記録上の C-1 ではなく、読み取った C-5 がふさいでいる
        var f = Setup();
        InStocker(f, "C-1", 1);
        var c5 = AtStation(f, "C-5");
        await f.Service.ReportIdMismatchAsync(Device, Slot(1), c5, None);
        var n = AtStation(f, "N-1");

        var free = f.Service.EvaluateDevice(Device).FreeSlots;
        await f.Service.OnTransferPermissionRequestedAsync(Permission(Transport1, Endpoints.Device(Device), TransferSide.Unload, n), None);

        Assert.Equal(2, free);
        Assert.Equal([new CargoSlot(n, Slot(2))], Assert.Single(f.Devices.Grants));
    }

    [Fact]
    public async Task 照合のエラーの消耗品は不整合としてほかの回収より先に返す()
    {
        var f = Setup();
        InStocker(f, "C-1", 1);
        var z = new ConsumableId("A-0");
        f.Service.RegisterConsumable(z, VarietyQ, OtherProcess, ConsumableCondition.New, new InStocker(Device, Slot(2)));
        var c5 = AtStation(f, "C-5");
        await f.Service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 1 }, None);

        await f.Service.ReportIdMismatchAsync(Device, Slot(1), c5, None);

        Assert.Equal(
            [new DeviceRecovery(c5, VarietyA, Process, RecoveryReason.Mismatch), new DeviceRecovery(z, VarietyQ, OtherProcess, RecoveryReason.NotInSetting)],
            f.Service.EvaluateDevice(Device).Recoveries);
    }

    [Fact]
    public async Task 設定がまだない装置でも不整合の消耗品は回収を返す()
    {
        var f = Setup();
        InStocker(f, "C-1", 1);
        var c5 = AtStation(f, "C-5");

        await f.Service.ReportIdMismatchAsync(Device, Slot(1), c5, None);

        Assert.Equal([RecoveryReason.Mismatch], f.Service.EvaluateDevice(Device).Recoveries.Select(r => r.Reason));
    }

    [Fact]
    public async Task 読み取ったIDが未登録なら記録上そのスロットにあった消耗品の加工の種類で登録する()
    {
        var f = Setup();
        InStocker(f, "C-1", 1);
        var x = new ConsumableId("X-1");

        await f.Service.ReportIdMismatchAsync(Device, Slot(1), x, None);

        var view = f.Service.GetConsumable(x);
        Assert.Null(view.Variety);
        Assert.Equal(Process, view.ProcessType);
        Assert.Equal(new UnknownLocation(Device, Slot(1)), view.Location);
        Assert.True(view.NeedsCheck);
        Assert.Equal([new DeviceRecovery(x, null, Process, RecoveryReason.Mismatch)], f.Service.EvaluateDevice(Device).Recoveries);
    }

    [Fact]
    public async Task 記録上何もなく未登録なら装置の設定にある加工の種類が1つのときそれを使う()
    {
        var f = Setup();
        await f.Service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 1 }, None);
        var x = new ConsumableId("X-1");

        await f.Service.ReportIdMismatchAsync(Device, Slot(1), x, None);

        Assert.Equal(Process, f.Service.GetConsumable(x).ProcessType);
    }

    [Fact]
    public async Task 記録上何もなく未登録で装置の設定にある加工の種類が1つでなければエラーにする()
    {
        var f = Setup();
        await f.Service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 1, [VarietyQ] = 1 }, None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Service.ReportIdMismatchAsync(Device, Slot(1), new ConsumableId("X-1"), None));
    }

    [Fact]
    public async Task 所在不明にした消耗品に付いていた予約を解く()
    {
        // C-5 は D1 へ届ける予約をしていた。所在不明になったので、届ける途中の数から外れる
        var f = Setup();
        InStocker(f, "C-1", 1);
        var c5 = AtStation(f, "C-5");
        await f.Service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 2 }, None);
        f.Service.ReserveForDelivery(Device, [c5]);

        await f.Service.ReportIdMismatchAsync(Device, Slot(1), c5, None);

        Assert.Equal(new Dictionary<VarietyId, int> { [VarietyA] = 2 }, f.Service.EvaluateDevice(Device).Shortages);
    }

    [Fact]
    public async Task 不整合の消耗品を回収する許可にはスロットを入れステーションでは要確認として届ける候補から外す()
    {
        var f = Setup();
        InStocker(f, "C-1", 1);
        var c5 = AtStation(f, "C-5");
        await f.Service.ReportIdMismatchAsync(Device, Slot(1), c5, None);

        await f.Service.OnTransferPermissionRequestedAsync(Permission(Transport1, Endpoints.Device(Device), TransferSide.Load, c5), None);
        await f.Service.ReportTransferCompletedAsync(Transport1, [c5], None);
        await f.Service.OnTransferPermissionRequestedAsync(Permission(Transport1, Endpoints.Station(Station), TransferSide.Unload, c5), None);
        await f.Service.ReportTransferCompletedAsync(Transport1, [c5], None);

        Assert.Equal([new CargoSlot(c5, Slot(1))], f.Devices.Grants[0]);
        var view = f.Service.GetConsumable(c5);
        Assert.Equal(new AtStation(Station), view.Location);
        Assert.True(view.NeedsCheck);
        Assert.Empty(f.Service.GetStationStock(VarietyA));
    }

    // ---- 交換の完了 ----

    [Fact]
    public async Task 交換の完了で読み取った消耗品を所在不明にし登録済みの交換後の消耗品をスロットの要確認にする()
    {
        var f = Setup();
        InStocker(f, "C-1", 1);
        var c5 = AtStation(f, "C-5");
        var c7 = AtStation(f, "C-7");
        await f.Service.ReportIdMismatchAsync(Device, Slot(1), c5, None);

        await f.Service.ReportReplacedAsync(Device, Slot(1), c7, None);

        Assert.Equal(new UnknownLocation(), f.Service.GetConsumable(c5).Location);
        var replaced = f.Service.GetConsumable(c7);
        Assert.Equal(new UnknownLocation(Device, Slot(1)), replaced.Location);
        Assert.True(replaced.NeedsCheck);
        Assert.Equal([new DeviceRecovery(c7, VarietyA, Process, RecoveryReason.Mismatch)], f.Service.EvaluateDevice(Device).Recoveries);
    }

    [Fact]
    public async Task 交換後のIDが未登録ならそのスロットにあった消耗品の加工の種類で登録する()
    {
        var f = Setup();
        InStocker(f, "C-1", 1);
        var c5 = AtStation(f, "C-5");
        await f.Service.ReportIdMismatchAsync(Device, Slot(1), c5, None);
        var y = new ConsumableId("Y-1");

        await f.Service.ReportReplacedAsync(Device, Slot(1), y, None);

        var view = f.Service.GetConsumable(y);
        Assert.Equal((null, Process), (view.Variety, view.ProcessType));
        Assert.Equal(new UnknownLocation(Device, Slot(1)), view.Location);
    }

    // ---- 段取り替え ----

    [Fact]
    public async Task 段取り替えで設定に含まれていない品種は上位システムにエラーを知らせる()
    {
        var f = Setup();
        await f.Service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 0 }, None);

        await f.Service.ReportSetupRequestedAsync(Device, VarietyA, None);
        await f.Service.ReportSetupRequestedAsync(Device, VarietyQ, None);
        await f.Service.ReportSetupRequestedAsync(Device, VarietyA, None);

        Assert.Equal([(Device, VarietyA), (Device, VarietyQ), (Device, VarietyA)], f.Upstream.SetupErrors);
    }

    [Fact]
    public async Task 段取り替えで設定に1個以上ある品種はエラーにしない()
    {
        var f = Setup();
        await f.Service.SetStockerSettingAsync(Device, new Dictionary<VarietyId, int> { [VarietyA] = 1 }, None);

        await f.Service.ReportSetupRequestedAsync(Device, VarietyA, None);

        Assert.Empty(f.Upstream.SetupErrors);
    }

    [Fact]
    public async Task 設定がまだない装置への段取り替えはエラーを知らせる()
    {
        var f = Setup();

        await f.Service.ReportSetupRequestedAsync(Device, VarietyA, None);

        Assert.Equal([(Device, VarietyA)], f.Upstream.SetupErrors);
    }

    // ---- 変化の知らせ ----

    [Fact]
    public async Task 取り込みと戻したと照合のエラーと交換で在庫が変わったと知らせ段取り替えでは知らせない()
    {
        var f = Setup();
        var c1 = InStocker(f, "C-1", 1);
        var c2 = InStocker(f, "C-2", 2);
        var c7 = AtStation(f, "C-7");

        await f.Service.ReportTakenIntoProcessingAsync(Device, c1, None);
        await f.Service.ReportReturnedToStockerAsync(Device, c1, Slot(1), None);
        await f.Service.ReportIdMismatchAsync(Device, Slot(2), new ConsumableId("X-1"), None);
        await f.Service.ReportReplacedAsync(Device, Slot(2), c7, None);
        await f.Service.ReportSetupRequestedAsync(Device, VarietyA, None);

        Assert.Equal(4, f.Publisher.Published.OfType<StockChanged>().Count());
        Assert.Equal(new UnknownLocation(), f.Service.GetConsumable(c2).Location);
    }
}
