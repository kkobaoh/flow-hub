using FlowHub.Map;
using FlowHub.Traffic.Domain;

namespace FlowHub.Traffic.Tests;

/// <summary>
/// パネルごとの通る順番を守らせる仕組みのテスト。
/// </summary>
public class PassingOrderGateTests
{
    private static readonly AgvId Agv1 = new("AGV-1");
    private static readonly AgvId Agv2 = new("AGV-2");
    private static readonly PanelId M = new("M");

    private static PanelId[] Panels(params string[] ids) => ids.Select(id => new PanelId(id)).ToArray();

    /// <summary>AGV ごとの、これから使うパネルの並びを、テストで書き換えられる門。</summary>
    private static (PassingOrderGate Gate, Dictionary<AgvId, IReadOnlyList<PanelId>> Remaining) Gate(params (string Panel, string[] Order)[] order)
    {
        var remaining = new Dictionary<AgvId, IReadOnlyList<PanelId>>();
        var gate = new PassingOrderGate(agv => remaining.TryGetValue(agv, out var panels) ? panels : null);
        gate.Update(order.ToDictionary(o => o.Panel, o => (IReadOnlyList<string>)o.Order));
        return (gate, remaining);
    }

    [Fact]
    public void 先に通る予定のAGVが通り終えるまで自分の番にならない()
    {
        var (gate, remaining) = Gate(("M", ["AGV-1", "AGV-2"]));
        remaining[Agv1] = Panels("N", "M", "S");
        remaining[Agv2] = Panels("E", "M", "W");

        var beforePassing = gate.IsMyTurn(Agv2, Panels("E", "M"));
        remaining[Agv1] = Panels("S"); // M を通り終えた
        var afterPassing = gate.IsMyTurn(Agv2, Panels("E", "M"));

        Assert.False(beforePassing);
        Assert.True(afterPassing);
        Assert.True(gate.IsMyTurn(Agv1, Panels("S")));
    }

    [Fact]
    public void 同じパネルを2回通るAGVの間に入るAGVは1回目を通り終えたら自分の番になる()
    {
        // M を AGV-1、AGV-2、AGV-1 の順に通る
        var (gate, remaining) = Gate(("M", ["AGV-1", "AGV-2", "AGV-1"]));
        remaining[Agv1] = Panels("N", "M", "X", "M"); // まだ1回目の前
        remaining[Agv2] = Panels("E", "M");

        var beforeFirst = gate.IsMyTurn(Agv2, [M]);
        remaining[Agv1] = Panels("X", "M"); // 1回目を通り終えた
        var afterFirst = gate.IsMyTurn(Agv2, [M]);
        var agv1SecondTime = gate.IsMyTurn(Agv1, [M]);

        Assert.False(beforeFirst);
        Assert.True(afterFirst);
        Assert.False(agv1SecondTime); // 2回目は、AGV-2 が通り終えてから
    }

    [Fact]
    public void 計画にないパネルはいつでも自分の番()
    {
        var (gate, _) = Gate();

        Assert.True(gate.IsMyTurn(Agv1, [M]));
    }

    [Fact]
    public void 計画にないAGVが先に並んでいれば通り終えることはないので自分の番にならない()
    {
        // AGV-9 は動かせない障害物（交通整理には登録されていない）
        var (gate, remaining) = Gate(("M", ["AGV-9", "AGV-1"]));
        remaining[Agv1] = Panels("N", "M");

        Assert.False(gate.IsMyTurn(Agv1, [M]));
    }
}
