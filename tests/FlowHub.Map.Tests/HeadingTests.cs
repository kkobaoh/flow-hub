namespace FlowHub.Map.Tests;

public class HeadingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(359)]
    public void 角度は0度以上360度未満で作れる(int degrees)
    {
        var heading = new Heading(degrees);

        Assert.Equal(degrees, heading.Degrees);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(360)]
    public void 範囲外の角度は作れない(int degrees)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Heading(degrees));
    }

    [Theory]
    [InlineData(0, 180)]
    [InlineData(90, 270)]
    [InlineData(200, 20)]
    [InlineData(315, 135)]
    public void 逆向きは180度反対の角度(int degrees, int expected)
    {
        Assert.Equal(new Heading(expected), new Heading(degrees).Reverse());
    }
}
