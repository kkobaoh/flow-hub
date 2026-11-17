using FlowHub.Dispatching.Domain;
using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic;

namespace FlowHub.Dispatching.Tests;

public class TransportQueueTests
{
    private static readonly CarrierTypeId CarrierX = new("X");
    private static readonly ChargingPolicy Policy = new(20, 80);
    private static readonly DateTimeOffset BaseTime = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    private static TransportRequest Request(string cargo, int neededInMinutes) => new(
        Requester: "ToolPlanning",
        CargoRefs: [cargo],
        Source: new TransferPoint(new PanelId("S"), "station-1"),
        Destination: new TransferPoint(new PanelId("D"), "device-1"),
        Carrier: CarrierX,
        NeededBy: BaseTime.AddMinutes(neededInMinutes));

    private static AgvCandidate IdleAgv(string id) =>
        new(new AgvId(id), new Pose("A", 0), IsIdle: true, BatteryPercent: 100, RecoveringCharge: false, CarrierX, CumulativeDistanceMm: 0);

    private static TimeSpan? AnyCost(AgvCandidate candidate) => TimeSpan.FromSeconds(10);

    [Fact]
    public void 頼まれた搬送には1から順に番号を付ける()
    {
        var queue = new TransportQueue();

        var first = queue.Add(Request("C-1", 10));
        var second = queue.Add(Request("C-2", 10));

        Assert.Equal(new TransportId(1), first.Id);
        Assert.Equal(new TransportId(2), second.Id);
    }

    [Fact]
    public void 必要時刻が早い搬送から割り当てる()
    {
        var queue = new TransportQueue();
        var later = queue.Add(Request("C-1", 30));
        var sooner = queue.Add(Request("C-2", 10));

        var assigned = queue.AssignWaiting([IdleAgv("AGV-1")], Policy, AnyCost);

        Assert.Equal([sooner.Id], assigned.Select(a => a.Transport.Id));
        Assert.Equal(TransportStage.Waiting, later.Stage);
    }

    [Fact]
    public void 必要時刻が同じなら頼まれた順に割り当てる()
    {
        var queue = new TransportQueue();
        var first = queue.Add(Request("C-1", 10));
        queue.Add(Request("C-2", 10));

        var assigned = queue.AssignWaiting([IdleAgv("AGV-1")], Policy, AnyCost);

        Assert.Equal([first.Id], assigned.Select(a => a.Transport.Id));
    }

    [Fact]
    public void AGVの1台には搬送を1つだけ割り当てる()
    {
        var queue = new TransportQueue();
        queue.Add(Request("C-1", 10));
        queue.Add(Request("C-2", 20));

        var assigned = queue.AssignWaiting([IdleAgv("AGV-1"), IdleAgv("AGV-2")], Policy, AnyCost);

        Assert.Equal(2, assigned.Count);
        Assert.NotEqual(assigned[0].Transport.AssignedAgv, assigned[1].Transport.AssignedAgv);
    }

    [Fact]
    public void 割り当てたときのコマンドを返す()
    {
        var queue = new TransportQueue();
        var transport = queue.Add(Request("C-1", 10));

        var assigned = queue.AssignWaiting([IdleAgv("AGV-1")], Policy, AnyCost);

        Assert.Equal([new MoveAgv(new AgvId("AGV-1"), new PanelId("S"))], assigned.Single().Actions);
        Assert.Equal(TransportStage.MovingToSource, transport.Stage);
    }

    [Fact]
    public void 割り当てられるAGVがいなければ待ち行列に残す()
    {
        var queue = new TransportQueue();
        var transport = queue.Add(Request("C-1", 10));

        var assigned = queue.AssignWaiting([], Policy, AnyCost);

        Assert.Empty(assigned);
        Assert.Equal(TransportStage.Waiting, transport.Stage);
        Assert.Equal([transport], queue.Waiting);
    }

    [Fact]
    public void 割り当てた搬送は待ち行列から外れる()
    {
        var queue = new TransportQueue();
        queue.Add(Request("C-1", 10));

        queue.AssignWaiting([IdleAgv("AGV-1")], Policy, AnyCost);

        Assert.Empty(queue.Waiting);
    }

    [Fact]
    public void 割り当て前の搬送は取りやめると待ち行列から外れる()
    {
        var queue = new TransportQueue();
        var transport = queue.Add(Request("C-1", 10));

        queue.Cancel(transport.Id);

        Assert.Empty(queue.Waiting);
        Assert.Equal(TransportStage.Canceled, transport.Stage);
    }

    [Fact]
    public void 待ち行列にない搬送は取りやめられない()
    {
        var queue = new TransportQueue();

        Assert.Throws<InvalidOperationException>(() => queue.Cancel(new TransportId(99)));
    }
}
