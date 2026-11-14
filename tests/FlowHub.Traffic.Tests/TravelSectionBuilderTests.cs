using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic.Domain;

namespace FlowHub.Traffic.Tests;

public class TravelSectionBuilderTests
{
    private static MoveStep Forward(string from, string to, int heading, int distanceMm = 800) =>
        new(MoveDirection.Forward, from, to, distanceMm, new Pose(to, heading));

    private static MoveStep Backward(string from, string to, int heading, int distanceMm = 800) =>
        new(MoveDirection.Backward, from, to, distanceMm, new Pose(to, heading));

    private static RotateStep Rotate(string at, int heading) => new(at, heading, new Pose(at, heading));

    private static PanelId[] Panels(params string[] ids) => ids.Select(id => new PanelId(id)).ToArray();

    [Fact]
    public void 動作がなければ区間もない()
    {
        var sections = TravelSectionBuilder.Build(new Pose("A", 0), []);

        Assert.Empty(sections);
    }

    [Fact]
    public void 回転がなければ1つの区間になる()
    {
        var sections = TravelSectionBuilder.Build(new Pose("A", 0), [Forward("A", "B", 0), Forward("B", "C", 0)]);

        var section = Assert.Single(sections);
        Assert.Equal([new ForwardCommand(800), new ForwardCommand(800)], section.Commands);
    }

    [Fact]
    public void 区間は回転で区切り回転は区間の最後に入る()
    {
        // 例：「800mm 前進、800mm 前進、絶対角度 90度へ回転」
        var sections = TravelSectionBuilder.Build(
            new Pose("A", 0),
            [Forward("A", "B", 0), Forward("B", "C", 0), Rotate("C", 90), Forward("C", "D", 90, distanceMm: 1200)]);

        Assert.Equal(2, sections.Count);
        Assert.Equal([new ForwardCommand(800), new ForwardCommand(800), new RotateCommand(90)], sections[0].Commands);
        Assert.Equal([new ForwardCommand(1200)], sections[1].Commands);
    }

    [Fact]
    public void 最初に回転するときは回転だけの区間になる()
    {
        var sections = TravelSectionBuilder.Build(new Pose("A", 0), [Rotate("A", 90), Forward("A", "B", 90)]);

        Assert.Equal([new RotateCommand(90)], sections[0].Commands);
        Assert.Equal([new ForwardCommand(800)], sections[1].Commands);
    }

    [Fact]
    public void 回転で終わる経路の後ろに空の区間を作らない()
    {
        var sections = TravelSectionBuilder.Build(new Pose("A", 0), [Forward("A", "B", 0), Rotate("B", 90)]);

        var section = Assert.Single(sections);
        Assert.Equal([new ForwardCommand(800), new RotateCommand(90)], section.Commands);
    }

    [Fact]
    public void 後退は後退の指示になる()
    {
        var sections = TravelSectionBuilder.Build(new Pose("T", 0), [Backward("T", "A", 0)]);

        Assert.Equal([new BackwardCommand(800)], sections.Single().Commands);
    }

    [Fact]
    public void 前進と後退が切り替わるところでも区間を区切る()
    {
        // 前進と後退は別の指示なので、回転がなくても区切る
        var sections = TravelSectionBuilder.Build(
            new Pose("A", 0),
            [Forward("A", "B", 0), Backward("B", "A", 0), Backward("A", "C", 0), Forward("C", "A", 0)]);

        Assert.Equal(3, sections.Count);
        Assert.Equal([new ForwardCommand(800)], sections[0].Commands);
        Assert.Equal([new BackwardCommand(800), new BackwardCommand(800)], sections[1].Commands);
        Assert.Equal([new ForwardCommand(800)], sections[2].Commands);
    }

    [Fact]
    public void 前進と後退の切り替えで区切った区間も通るパネルと終えたあとの姿勢を持つ()
    {
        var sections = TravelSectionBuilder.Build(
            new Pose("T", 0),
            [Backward("T", "A", 0), Forward("A", "T", 0)]);

        Assert.Equal(Panels("T", "A"), sections[0].Panels);
        Assert.Equal(new Pose("A", 0), sections[0].End);
        Assert.Equal(Panels("A", "T"), sections[1].Panels);
        Assert.Equal(new Pose("T", 0), sections[1].End);
    }

    [Fact]
    public void 区間には通るパネルが出発のパネルから順に入る()
    {
        // 予約に使う：その区間で AGV がいる可能性のあるパネル
        var sections = TravelSectionBuilder.Build(
            new Pose("A", 0),
            [Forward("A", "B", 0), Forward("B", "C", 0), Rotate("C", 90), Forward("C", "D", 90)]);

        Assert.Equal(Panels("A", "B", "C"), sections[0].Panels);
        Assert.Equal(Panels("C", "D"), sections[1].Panels);
    }

    private sealed record UnknownStep(Pose After) : RouteStep(After);

    [Fact]
    public void 未知の種類の動作は受け付けない()
    {
        Assert.Throws<ArgumentException>(() => TravelSectionBuilder.Build(new Pose("A", 0), [new UnknownStep(new Pose("A", 0))]));
    }

    [Fact]
    public void 区間には区間を終えたあとの姿勢が入る()
    {
        var sections = TravelSectionBuilder.Build(
            new Pose("A", 0),
            [Forward("A", "B", 0), Rotate("B", 90), Forward("B", "C", 90)]);

        Assert.Equal(new Pose("B", 90), sections[0].End);
        Assert.Equal(new Pose("C", 90), sections[1].End);
    }
}
