namespace FlowHub.Routing.Tests;

public class RouteModelTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(360)]
    public void 姿勢の角度は0度以上360度未満(int heading)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pose("A", heading));
    }

    [Theory]
    [InlineData(0, 250, 90)]
    [InlineData(500, 0, 90)]
    [InlineData(500, 250, 0)]
    [InlineData(-1, 250, 90)]
    public void 速度は正の値でなければならない(double forward, double backward, double rotation)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MotionProfile(forward, backward, rotation));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(360)]
    public void 辺の向きは0度以上360度未満(int heading)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RouteEdge("A", "B", 800, heading, OneWay: false));
    }

    [Fact]
    public void グラフのノードは重複できない()
    {
        var node = new RouteNode("A", CanRotate: true, ForwardEntryOnly: false, BackwardExitOnly: false);

        Assert.Throws<ArgumentException>(() => new RouteGraph([node, node], []));
    }

    [Fact]
    public void グラフの辺は存在するノードを指さなければならない()
    {
        var node = new RouteNode("A", CanRotate: true, ForwardEntryOnly: false, BackwardExitOnly: false);

        Assert.Throws<ArgumentException>(() => new RouteGraph([node], [new RouteEdge("A", "X", 800, 0, OneWay: false)]));
    }

    [Fact]
    public void 辺の距離は正の値でなければならない()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RouteEdge("A", "B", 0, 0, OneWay: false));
    }
}
