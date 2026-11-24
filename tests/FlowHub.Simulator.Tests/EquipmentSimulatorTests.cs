using FlowHub.Consumables;
using FlowHub.Dispatching;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowHub.Simulator.Tests;

/// <summary>
/// 装置・ステーション・上位システムのシミュレーター（自動で完了を返す）のテスト。
/// </summary>
public class EquipmentSimulatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly DeviceId Device = new("DEV-1");
    private static readonly StationId Station1 = new("ST-1");
    private static readonly StationId Station2 = new("ST-2");
    private static readonly VarietyId Variety = new("V");
    private static readonly ConsumableId C1 = new("C-1");
    private static readonly ConsumableId C2 = new("C-2");
    private static readonly TransportId Transport = new(7);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>シミュレーターからの通知を記録する。</summary>
    private sealed class RecordingTarget : ISimulationTarget
    {
        public List<string> Calls { get; } = [];

        public int FailuresLeft { get; set; }

        public IReadOnlyList<StationId> Stations { get; set; } = [Station2, Station1];

        public Task ReportTransferCompletedAsync(TransportId transport, IReadOnlyList<ConsumableId> transferred, CancellationToken cancellationToken) =>
            Record($"完了 {transport.Value} {string.Join(",", transferred)}");

        public Task ReportShippedFromStationAsync(StationId station, ConsumableId id, CancellationToken cancellationToken) =>
            Record($"出庫 {station} {id}");

        public void RegisterExpectedArrival(ConsumableId id, VarietyId variety) =>
            Record($"予定 {id} {variety}");

        public Task ReportReadAtStationAsync(StationId station, ConsumableId id, CancellationToken cancellationToken) =>
            Record($"読み取り {station} {id}");

        private Task Record(string call)
        {
            lock (Calls)
            {
                if (FailuresLeft > 0)
                {
                    FailuresLeft--;
                    throw new InvalidOperationException("失敗");
                }

                Calls.Add(call);
            }

            return Task.CompletedTask;
        }
    }

    private static readonly SimulatorOptions Options = new()
    {
        DeviceTransferDelay = TimeSpan.FromSeconds(10),
        StationTransferDelay = TimeSpan.FromSeconds(20),
        ShipmentDelay = TimeSpan.FromSeconds(30),
        ReplenishmentDelay = TimeSpan.FromSeconds(60),
    };

    private static (EquipmentSimulator Simulator, RecordingTarget Target, ManualTimeProvider Clock, List<string> AfterNotified) Setup(SimulatorOptions? options = null)
    {
        var clock = new ManualTimeProvider(Start);
        var simulator = new EquipmentSimulator(options ?? Options, clock, NullLogger<EquipmentSimulator>.Instance);
        var target = new RecordingTarget();
        var afterNotified = new List<string>();
        simulator.Attach(target, _ =>
        {
            lock (afterNotified)
            {
                afterNotified.Add("届ける");
            }

            return Task.CompletedTask;
        });
        return (simulator, target, clock, afterNotified);
    }

    [Fact]
    public void 既定の待ち時間は装置とステーションの受け渡しと出庫が10秒で補充が60秒()
    {
        var options = new SimulatorOptions();

        Assert.Equal(TimeSpan.FromSeconds(10), options.DeviceTransferDelay);
        Assert.Equal(TimeSpan.FromSeconds(10), options.StationTransferDelay);
        Assert.Equal(TimeSpan.FromSeconds(10), options.ShipmentDelay);
        Assert.Equal(TimeSpan.FromSeconds(60), options.ReplenishmentDelay);
        Assert.Null(options.ReplenishmentStation);
    }

    [Fact]
    public async Task 装置への受け渡しの許可は装置の待ち時間のあとに頼んだとおりの完了を通知する()
    {
        var (simulator, target, clock, _) = Setup();
        IDeviceGateway devices = simulator;
        await devices.GrantTransferAsync(Device, Transport, TransferSide.Unload, [new CargoSlot(C1, new SlotRef(1, 1)), new CargoSlot(C2, new SlotRef(1, 2))], Token);

        clock.Advance(TimeSpan.FromSeconds(9));
        var before = target.Calls.Count;
        clock.Advance(TimeSpan.FromSeconds(1));
        await simulator.WhenIdleAsync();

        Assert.Equal(0, before);
        Assert.Equal(["完了 7 C-1,C-2"], target.Calls);
    }

    [Fact]
    public async Task ステーションへの受け渡しの許可はステーションの待ち時間のあとに完了を通知する()
    {
        var (simulator, target, clock, _) = Setup();
        IStationGateway stations = simulator;
        await stations.GrantTransferAsync(Station1, Transport, TransferSide.Load, [C1], Token);

        clock.Advance(TimeSpan.FromSeconds(19));
        var before = target.Calls.Count;
        clock.Advance(TimeSpan.FromSeconds(1));
        await simulator.WhenIdleAsync();

        Assert.Equal(0, before);
        Assert.Equal(["完了 7 C-1"], target.Calls);
    }

    [Fact]
    public async Task 出庫の頼みは出庫の待ち時間のあとに出たと通知する()
    {
        var (simulator, target, clock, _) = Setup();
        IStationGateway stations = simulator;
        await stations.RequestShipmentAsync(Station1, C1, Token);

        clock.Advance(TimeSpan.FromSeconds(29));
        var before = target.Calls.Count;
        clock.Advance(TimeSpan.FromSeconds(1));
        await simulator.WhenIdleAsync();

        Assert.Equal(0, before);
        Assert.Equal(["出庫 ST-1 C-1"], target.Calls);
    }

    [Fact]
    public async Task 補充の頼みは待ち時間のあとに新品を新しいIDで入庫の予定に登録してIDの順で一番目のステーションで読み取らせる()
    {
        var (simulator, target, clock, _) = Setup();
        IUpstreamGateway upstream = simulator;
        await upstream.RequestReplenishmentAsync(Variety, 2, Token);

        clock.Advance(TimeSpan.FromSeconds(59));
        var before = target.Calls.Count;
        clock.Advance(TimeSpan.FromSeconds(1));
        await simulator.WhenIdleAsync();

        Assert.Equal(0, before);
        Assert.Equal(["予定 SIM-V-1 V", "読み取り ST-1 SIM-V-1", "予定 SIM-V-2 V", "読み取り ST-1 SIM-V-2"], target.Calls);
    }

    [Fact]
    public async Task 補充で入庫するステーションは設定で指定できる()
    {
        var (simulator, target, clock, _) = Setup(Options with { ReplenishmentStation = "ST-2" });
        IUpstreamGateway upstream = simulator;
        await upstream.RequestReplenishmentAsync(Variety, 1, Token);

        clock.Advance(TimeSpan.FromSeconds(60));
        await simulator.WhenIdleAsync();

        Assert.Equal(["予定 SIM-V-1 V", "読み取り ST-2 SIM-V-1"], target.Calls);
    }

    [Fact]
    public async Task 補充のIDの連番は品種ごとに続けて振る()
    {
        var (simulator, target, clock, _) = Setup();
        IUpstreamGateway upstream = simulator;
        await upstream.RequestReplenishmentAsync(Variety, 1, Token);
        await upstream.RequestReplenishmentAsync(Variety, 1, Token);
        await upstream.RequestReplenishmentAsync(new VarietyId("W"), 1, Token);

        clock.Advance(TimeSpan.FromSeconds(60));
        await simulator.WhenIdleAsync();

        Assert.Contains("予定 SIM-V-1 V", target.Calls);
        Assert.Contains("予定 SIM-V-2 V", target.Calls);
        Assert.Contains("予定 SIM-W-1 W", target.Calls);
    }

    [Fact]
    public async Task 段取り替えのエラーの知らせは何も起こさない()
    {
        var (simulator, target, clock, afterNotified) = Setup();
        IUpstreamGateway upstream = simulator;

        await upstream.NotifySetupErrorAsync(Device, Variety, Token);
        clock.Advance(TimeSpan.FromMinutes(10));
        await simulator.WhenIdleAsync();

        Assert.Empty(target.Calls);
        Assert.Empty(afterNotified);
    }

    [Fact]
    public async Task 通知のたびに組み立てが渡した後処理を呼ぶ()
    {
        var (simulator, _, clock, afterNotified) = Setup();
        IDeviceGateway devices = simulator;
        await devices.GrantTransferAsync(Device, Transport, TransferSide.Load, [new CargoSlot(C1, new SlotRef(1, 1))], Token);

        clock.Advance(TimeSpan.FromSeconds(10));
        await simulator.WhenIdleAsync();

        Assert.Equal(["届ける"], afterNotified);
    }

    [Fact]
    public async Task 通知が失敗しても止まらずにほかの頼みは続ける()
    {
        var (simulator, target, clock, _) = Setup();
        target.FailuresLeft = 1;
        IStationGateway stations = simulator;
        await stations.RequestShipmentAsync(Station1, C1, Token);
        await stations.RequestShipmentAsync(Station1, C2, Token);

        clock.Advance(TimeSpan.FromSeconds(30));
        await simulator.WhenIdleAsync();

        Assert.Single(target.Calls);
    }
}
