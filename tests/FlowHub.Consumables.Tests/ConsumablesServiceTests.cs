using FlowHub.BuildingBlocks;
using FlowHub.Consumables.Application;
using FlowHub.Consumables.Domain;
using FlowHub.Dispatching;
using FlowHub.Map;

namespace FlowHub.Consumables.Tests;

public class ConsumablesServiceTests
{
    private static readonly DeviceId Device = new("D1");
    private static readonly StationId Station = new("S1");
    private static readonly ProcessTypeId Process = new("P");
    private static readonly VarietyId VarietyA = new("A");
    private static readonly ConsumableId C1 = new("C-1");
    private static readonly ConsumableId C2 = new("C-2");
    private static readonly ConsumableId C3 = new("C-3");
    private static readonly ConsumableId C9 = new("C-9");
    private static readonly TransportId Transport1 = new(1);
    private static readonly TransportId Transport2 = new(2);
    private static readonly CancellationToken None = CancellationToken.None;

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeDispatching : IDispatchingService
    {
        public List<TransportId> TransferCompleted { get; } = [];

        public Task<TransportId> RequestTransportAsync(TransportOrder order, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> CancelTransportAsync(TransportId id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task ReportTransferCompletedAsync(TransportId id, CancellationToken cancellationToken)
        {
            TransferCompleted.Add(id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDeviceGateway : IDeviceGateway
    {
        public List<(DeviceId Device, TransportId Transport, TransferSide Side, IReadOnlyList<CargoSlot> Cargo)> Grants { get; } = [];

        public Task GrantTransferAsync(DeviceId device, TransportId transport, TransferSide side, IReadOnlyList<CargoSlot> cargo, CancellationToken cancellationToken)
        {
            Grants.Add((device, transport, side, cargo));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStationGateway : IStationGateway
    {
        public List<(StationId Station, TransportId Transport, TransferSide Side, IReadOnlyList<ConsumableId> Cargo)> Grants { get; } = [];

        public Task GrantTransferAsync(StationId station, TransportId transport, TransferSide side, IReadOnlyList<ConsumableId> cargo, CancellationToken cancellationToken)
        {
            Grants.Add((station, transport, side, cargo));
            return Task.CompletedTask;
        }

        public Task RequestShipmentAsync(StationId station, ConsumableId consumable, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed record Fixture(ConsumablesService Service, FakeDispatching Dispatching, FakeDeviceGateway Devices, FakeStationGateway Stations);

    /// <summary>
    /// 装置 D1（ストッカー2つ、スロット2つずつ）と、ステーション S1。C-1、C-2 はステーションに、C-9 は装置のストッカー1のスロット1にある。
    /// </summary>
    private static Fixture Setup()
    {
        var dispatching = new FakeDispatching();
        var devices = new FakeDeviceGateway();
        var stations = new FakeStationGateway();
        var service = new ConsumablesService(dispatching, devices, stations, new IgnoringReplenishment(), new IgnoringPublisher(), new FixedTimeProvider());
        service.RegisterDevice(new DeviceLayout(Device, new PanelId("D"), SlotsPerStocker: [2, 2]));
        service.RegisterStation(new StationLayout(Station, new PanelId("S")));
        service.RegisterProcessType(new ProcessTypeSettings(Process, new CarrierTypeId("X"), CarrierCapacity: 2));
        service.RegisterConsumable(C1, VarietyA, Process, ConsumableCondition.New, new AtStation(Station));
        service.RegisterConsumable(C2, VarietyA, Process, ConsumableCondition.New, new AtStation(Station));
        service.RegisterConsumable(C3, VarietyA, Process, ConsumableCondition.New, new AtStation(Station));
        service.RegisterConsumable(C9, VarietyA, Process, ConsumableCondition.New, new InStocker(Device, new SlotRef(1, 1)));
        return new Fixture(service, dispatching, devices, stations);
    }

    private static TransferPermissionRequested Permission(
        TransportId transport, string endpoint, TransferSide side, params ConsumableId[] cargo) =>
        new(EventMetadata.Start("test", new FixedTimeProvider()), transport, ConsumablesService.Requester,
            cargo.Select(c => c.Value).ToArray(), endpoint, side);

    // ---- 搬送の許可 ----

    [Fact]
    public async Task ステーションで積み込む許可をステーションに出す()
    {
        var f = Setup();

        await f.Service.OnTransferPermissionRequestedAsync(Permission(Transport1, Endpoints.Station(Station), TransferSide.Load, C1, C2), None);

        var grant = Assert.Single(f.Stations.Grants);
        Assert.Equal((Station, Transport1, TransferSide.Load), (grant.Station, grant.Transport, grant.Side));
        Assert.Equal([C1, C2], grant.Cargo);
    }

    [Fact]
    public async Task 装置で降ろす許可には空いているうち番号が一番小さいスロットを入れる()
    {
        // ストッカー1のスロット1は C-9 がふさいでいる
        var f = Setup();

        await f.Service.OnTransferPermissionRequestedAsync(Permission(Transport1, Endpoints.Device(Device), TransferSide.Unload, C1, C2), None);

        var grant = Assert.Single(f.Devices.Grants);
        Assert.Equal([new CargoSlot(C1, new SlotRef(1, 2)), new CargoSlot(C2, new SlotRef(2, 1))], grant.Cargo);
    }

    [Fact]
    public async Task 入れ先に決めたスロットは次の割り当てで使わない()
    {
        var f = Setup();
        await f.Service.OnTransferPermissionRequestedAsync(Permission(Transport1, Endpoints.Device(Device), TransferSide.Unload, C1), None);

        await f.Service.OnTransferPermissionRequestedAsync(Permission(Transport2, Endpoints.Device(Device), TransferSide.Unload, C2), None);

        Assert.Equal([new CargoSlot(C2, new SlotRef(2, 1))], f.Devices.Grants[^1].Cargo);
    }

    [Fact]
    public async Task 装置で回収する許可には取り出すスロットを入れる()
    {
        var f = Setup();

        await f.Service.OnTransferPermissionRequestedAsync(Permission(Transport1, Endpoints.Device(Device), TransferSide.Load, C9), None);

        Assert.Equal([new CargoSlot(C9, new SlotRef(1, 1))], Assert.Single(f.Devices.Grants).Cargo);
    }

    [Fact]
    public async Task 要求元が違う搬送の許可は扱わない()
    {
        var f = Setup();
        var other = Permission(Transport1, Endpoints.Station(Station), TransferSide.Load, C1) with { Requester = "work" };

        await f.Service.OnTransferPermissionRequestedAsync(other, None);

        Assert.Empty(f.Stations.Grants);
    }

    [Fact]
    public async Task 空いているスロットが足りなければ許可を出さない()
    {
        var f = Setup();
        var many = Enumerable.Range(1, 4).Select(i => new ConsumableId($"N-{i}")).ToArray();
        foreach (var id in many)
        {
            f.Service.RegisterConsumable(id, VarietyA, Process, ConsumableCondition.New, new AtStation(Station));
        }

        // 空いているのは 3 スロット
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Service.OnTransferPermissionRequestedAsync(Permission(Transport1, Endpoints.Device(Device), TransferSide.Unload, many), None));
        Assert.Empty(f.Devices.Grants);
    }

    [Fact]
    public async Task ストッカーにない消耗品は装置から回収できない()
    {
        var f = Setup();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Service.OnTransferPermissionRequestedAsync(Permission(Transport1, Endpoints.Device(Device), TransferSide.Load, C1), None));
    }

    [Fact]
    public void 登録されていない消耗品は参照できない()
    {
        var f = Setup();

        Assert.Throws<InvalidOperationException>(() => f.Service.GetConsumable(new ConsumableId("C-X")));
    }

    // ---- 受け渡しの完了 ----

    [Fact]
    public async Task 積み込みの完了で所在をAGV内部にして配車に伝える()
    {
        var f = Setup();
        await f.Service.OnTransferPermissionRequestedAsync(Permission(Transport1, Endpoints.Station(Station), TransferSide.Load, C1), None);

        var report = await f.Service.ReportTransferCompletedAsync(Transport1, [C1], None);

        Assert.False(report.HasMismatch);
        Assert.Equal(new OnAgv(Transport1), f.Service.GetConsumable(C1).Location);
        Assert.Equal([Transport1], f.Dispatching.TransferCompleted);
    }

    [Fact]
    public async Task 装置で降ろした完了で所在を受け渡し部にして入れ先のスロットを覚える()
    {
        var f = Setup();
        await f.Service.OnTransferPermissionRequestedAsync(Permission(Transport1, Endpoints.Device(Device), TransferSide.Unload, C1), None);

        await f.Service.ReportTransferCompletedAsync(Transport1, [C1], None);

        Assert.Equal(new AtTransferUnit(Device, new SlotRef(1, 2)), f.Service.GetConsumable(C1).Location);
    }

    [Fact]
    public async Task ステーションで降ろした完了で所在をステーションにする()
    {
        var f = Setup();
        await f.Service.OnTransferPermissionRequestedAsync(Permission(Transport1, Endpoints.Device(Device), TransferSide.Load, C9), None);
        await f.Service.ReportTransferCompletedAsync(Transport1, [C9], None);
        await f.Service.OnTransferPermissionRequestedAsync(Permission(Transport1, Endpoints.Station(Station), TransferSide.Unload, C9), None);

        await f.Service.ReportTransferCompletedAsync(Transport1, [C9], None);

        Assert.Equal(new AtStation(Station), f.Service.GetConsumable(C9).Location);
    }

    [Fact]
    public async Task 回収で取り出したスロットは空く()
    {
        var f = Setup();
        await f.Service.OnTransferPermissionRequestedAsync(Permission(Transport1, Endpoints.Device(Device), TransferSide.Load, C9), None);
        await f.Service.ReportTransferCompletedAsync(Transport1, [C9], None);

        await f.Service.OnTransferPermissionRequestedAsync(Permission(Transport2, Endpoints.Device(Device), TransferSide.Unload, C1), None);

        Assert.Equal([new CargoSlot(C1, new SlotRef(1, 1))], f.Devices.Grants[^1].Cargo);
    }

    [Fact]
    public async Task 通知のIDが頼んだものと違ったら通知を正として所在を更新し食い違いを返す()
    {
        var f = Setup();
        await f.Service.OnTransferPermissionRequestedAsync(Permission(Transport1, Endpoints.Station(Station), TransferSide.Load, C1, C2), None);

        var report = await f.Service.ReportTransferCompletedAsync(Transport1, [C1, C3], None);

        Assert.True(report.HasMismatch);
        Assert.Equal([C2], report.Missing);
        Assert.Equal([C3], report.Unexpected);
        Assert.Equal(new OnAgv(Transport1), f.Service.GetConsumable(C3).Location);
        Assert.Equal(new AtStation(Station), f.Service.GetConsumable(C2).Location);
        Assert.Equal([Transport1], f.Dispatching.TransferCompleted);
    }

    [Fact]
    public async Task 知らない消耗品のIDが通知されたら食い違いとして返す()
    {
        var f = Setup();
        await f.Service.OnTransferPermissionRequestedAsync(Permission(Transport1, Endpoints.Station(Station), TransferSide.Load, C1), None);

        var report = await f.Service.ReportTransferCompletedAsync(Transport1, [C1, new ConsumableId("C-X")], None);

        Assert.Equal([new ConsumableId("C-X")], report.UnknownIds);
        Assert.True(report.HasMismatch);
    }

    [Fact]
    public async Task 許可を出していない搬送の完了は受け付けない()
    {
        var f = Setup();

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ReportTransferCompletedAsync(Transport1, [C1], None));
    }

    // ---- 登録 ----

    [Fact]
    public void 同じ消耗品は二重に登録できない()
    {
        var f = Setup();

        Assert.Throws<InvalidOperationException>(() =>
            f.Service.RegisterConsumable(C1, VarietyA, Process, ConsumableCondition.New, new AtStation(Station)));
    }

    [Fact]
    public void 加工の種類のキャリアに入る個数は1以上()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessTypeSettings(Process, new CarrierTypeId("X"), CarrierCapacity: 0));
    }

    [Fact]
    public void 装置のストッカーのスロットの数は1以上()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeviceLayout(Device, new PanelId("D"), SlotsPerStocker: [2, 0]));
    }

    [Fact]
    public void 装置とステーションと加工の種類のIDは空にできない()
    {
        Assert.Throws<ArgumentException>(() => new DeviceId(" "));
        Assert.Throws<ArgumentException>(() => new StationId(""));
        Assert.Throws<ArgumentException>(() => new ProcessTypeId(" "));
        Assert.Equal("D1", Device.ToString());
        Assert.Equal("S1", Station.ToString());
        Assert.Equal("P", Process.ToString());
    }
}
