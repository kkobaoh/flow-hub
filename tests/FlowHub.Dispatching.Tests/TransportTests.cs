using FlowHub.Dispatching.Domain;
using FlowHub.Map;
using FlowHub.Traffic;

namespace FlowHub.Dispatching.Tests;

public class TransportTests
{
    private static readonly AgvId Agv = new("AGV-1");
    private static readonly TransferPoint Station = new(new PanelId("S"), "station-1");
    private static readonly TransferPoint Device = new(new PanelId("D"), "device-1");

    private static readonly TransportRequest Delivery = new(
        Requester: "ToolPlanning",
        CargoRefs: ["C-1", "C-2"],
        Source: Station,
        Destination: Device,
        Carrier: new CarrierTypeId("X"),
        NeededBy: new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));

    private static Transport NewTransport() => new(new TransportId(1), Delivery);

    private static Transport AssignedTransport()
    {
        var transport = NewTransport();
        transport.Assign(Agv);
        return transport;
    }

    /// <summary>搬送元で、受け渡しの完了まで進めた搬送（次はキャリアを上げる）。</summary>
    private static Transport TransferredAtSource()
    {
        var transport = AssignedTransport();
        transport.OnArrived(Station.Panel);
        transport.OnCarrierLowered();
        transport.OnTransferCompleted();
        return transport;
    }

    /// <summary>搬送先に着くまで進めた搬送（次はキャリアを下げる）。</summary>
    private static Transport ArrivedAtDestination()
    {
        var transport = TransferredAtSource();
        transport.OnCarrierRaised();
        transport.OnArrived(Device.Panel);
        return transport;
    }

    // ---- 正常な流れ ----

    [Fact]
    public void 頼まれた搬送は割り当て待ち()
    {
        var transport = NewTransport();

        Assert.Equal(TransportStage.Waiting, transport.Stage);
        Assert.Null(transport.AssignedAgv);
    }

    [Fact]
    public void 割り当てるとAGVを搬送元へ動かす()
    {
        var transport = NewTransport();

        var actions = transport.Assign(Agv);

        Assert.Equal(TransportStage.MovingToSource, transport.Stage);
        Assert.Equal(Agv, transport.AssignedAgv);
        Assert.Equal([new MoveAgv(Agv, Station.Panel)], actions);
    }

    [Fact]
    public void 搬送元に着いたらキャリアを下げる()
    {
        var transport = AssignedTransport();

        var result = transport.OnArrived(Station.Panel);

        Assert.True(result.Accepted);
        Assert.Equal(TransportStage.LoweringAtSource, transport.Stage);
        Assert.Equal([new LowerCarrier(Agv)], result.Actions);
    }

    [Fact]
    public void キャリアが下がったら搬送元に搬送の許可を頼む()
    {
        var transport = AssignedTransport();
        transport.OnArrived(Station.Panel);

        var result = transport.OnCarrierLowered();

        Assert.Equal(TransportStage.TransferringAtSource, transport.Stage);
        Assert.Equal([new RequestTransferPermission(transport.Id, Station, TransferSide.Load)], result.Actions);
    }

    [Fact]
    public void 受け渡しが終わったらキャリアを上げる()
    {
        var transport = AssignedTransport();
        transport.OnArrived(Station.Panel);
        transport.OnCarrierLowered();

        var result = transport.OnTransferCompleted();

        Assert.Equal(TransportStage.RaisingAtSource, transport.Stage);
        Assert.Equal([new RaiseCarrier(Agv)], result.Actions);
    }

    [Fact]
    public void 搬送元でキャリアが上がったら搬送先へ動かす()
    {
        var transport = TransferredAtSource();

        var result = transport.OnCarrierRaised();

        Assert.Equal(TransportStage.MovingToDestination, transport.Stage);
        Assert.Equal([new MoveAgv(Agv, Device.Panel)], result.Actions);
    }

    [Fact]
    public void 搬送先でも同じ手順で降ろす()
    {
        var transport = ArrivedAtDestination();
        Assert.Equal(TransportStage.LoweringAtDestination, transport.Stage);

        var lowered = transport.OnCarrierLowered();
        Assert.Equal([new RequestTransferPermission(transport.Id, Device, TransferSide.Unload)], lowered.Actions);

        var transferred = transport.OnTransferCompleted();
        Assert.Equal([new RaiseCarrier(Agv)], transferred.Actions);
    }

    [Fact]
    public void 搬送先でキャリアが上がったら完了を知らせる()
    {
        var transport = ArrivedAtDestination();
        transport.OnCarrierLowered();
        transport.OnTransferCompleted();

        var result = transport.OnCarrierRaised();

        Assert.Equal(TransportStage.Completed, transport.Stage);
        Assert.Equal([new NotifyCompleted(transport.Id)], result.Actions);
    }

    [Fact]
    public void 割り当て済みの搬送はもう一度割り当てられない()
    {
        var transport = AssignedTransport();

        Assert.Throws<InvalidOperationException>(() => transport.Assign(new AgvId("AGV-2")));
    }

    // ---- 段階と合わない知らせ ----

    [Fact]
    public void 段階と合わない知らせは状態を変えない()
    {
        var transport = AssignedTransport();

        var result = transport.OnCarrierRaised();

        Assert.False(result.Accepted);
        Assert.Empty(result.Actions);
        Assert.Equal(TransportStage.MovingToSource, transport.Stage);
    }

    [Fact]
    public void 搬送元でないパネルへの到着は段階と合わない知らせ()
    {
        var transport = AssignedTransport();

        var result = transport.OnArrived(Device.Panel);

        Assert.False(result.Accepted);
        Assert.Equal(TransportStage.MovingToSource, transport.Stage);
    }

    // ---- 一時停止 ----

    [Fact]
    public void 一時停止中に届いた知らせは状態を進めるが次のコマンドは出さない()
    {
        var transport = AssignedTransport();
        transport.Pause();

        var result = transport.OnArrived(Station.Panel);

        Assert.True(result.Accepted);
        Assert.Equal(TransportStage.LoweringAtSource, transport.Stage);
        Assert.Empty(result.Actions);
    }

    [Fact]
    public void 再開すると一時停止中に出さなかったコマンドを出す()
    {
        var transport = AssignedTransport();
        transport.Pause();
        transport.OnArrived(Station.Panel);

        var actions = transport.Resume();

        Assert.Equal([new LowerCarrier(Agv)], actions);
    }

    [Fact]
    public void 一時停止の前に出したコマンドは再開しても出し直さない()
    {
        // 搬送元へ動かすコマンドは出してあり、AGV はアラームクリアのあと続きを走る
        var transport = AssignedTransport();
        transport.Pause();

        var actions = transport.Resume();

        Assert.Empty(actions);
    }

    [Fact]
    public void 一時停止中にキャリアが上がったら状態は進めて搬送先へは再開してから動かす()
    {
        var transport = TransferredAtSource();
        transport.Pause();

        var result = transport.OnCarrierRaised();

        Assert.Empty(result.Actions);
        Assert.Equal([new MoveAgv(Agv, Device.Panel)], transport.Resume());
    }

    [Fact]
    public void 一時停止中は搬送の許可も頼まない()
    {
        var transport = AssignedTransport();
        transport.OnArrived(Station.Panel);
        transport.Pause();

        var result = transport.OnCarrierLowered();

        Assert.Empty(result.Actions);
        Assert.Equal([new RequestTransferPermission(transport.Id, Station, TransferSide.Load)], transport.Resume());
    }

    [Fact]
    public void 一時停止中に受け渡しが終わったらキャリアは再開してから上げる()
    {
        var transport = AssignedTransport();
        transport.OnArrived(Station.Panel);
        transport.OnCarrierLowered();
        transport.Pause();

        transport.OnTransferCompleted();

        Assert.Equal([new RaiseCarrier(Agv)], transport.Resume());
    }

    // ---- 中断 ----

    [Fact]
    public void 異常になったら搬送を中断して知らせる()
    {
        var transport = TransferredAtSource();

        var actions = transport.Interrupt();

        Assert.Equal(TransportStage.Interrupted, transport.Stage);
        Assert.Equal(TransportStage.RaisingAtSource, transport.InterruptedAt);
        Assert.Equal([new NotifyInterrupted(transport.Id)], actions);
    }

    [Fact]
    public void 中断した搬送への知らせは段階と合わない知らせ()
    {
        var transport = AssignedTransport();
        transport.Interrupt();

        var result = transport.OnArrived(Station.Panel);

        Assert.False(result.Accepted);
        Assert.Equal(TransportStage.Interrupted, transport.Stage);
    }

    // ---- 取りやめ ----

    [Fact]
    public void 割り当て前なら取りやめられる()
    {
        var transport = NewTransport();

        transport.Cancel();

        Assert.Equal(TransportStage.Canceled, transport.Stage);
    }

    [Fact]
    public void 割り当てたあとはこの操作では取りやめられない()
    {
        var transport = AssignedTransport();

        Assert.Throws<InvalidOperationException>(() => transport.Cancel());
    }
}
