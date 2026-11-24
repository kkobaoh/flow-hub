namespace FlowHub.Traffic.Tests;

public class AgvIdTests
{
    [Fact]
    public void 同じ値のIDは等しい()
    {
        Assert.Equal(new AgvId("AGV-1"), new AgvId("AGV-1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void 空のIDは作れない(string value)
    {
        Assert.Throws<ArgumentException>(() => new AgvId(value));
    }

    [Fact]
    public void 文字列にするとIDの値になる()
    {
        Assert.Equal("AGV-1", new AgvId("AGV-1").ToString());
    }
}
