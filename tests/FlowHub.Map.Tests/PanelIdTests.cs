namespace FlowHub.Map.Tests;

public class PanelIdTests
{
    [Fact]
    public void 同じ値のIDは等しい()
    {
        Assert.Equal(new PanelId("P-001"), new PanelId("P-001"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void 空のIDは作れない(string value)
    {
        Assert.Throws<ArgumentException>(() => new PanelId(value));
    }

    [Fact]
    public void 文字列にするとIDの値になる()
    {
        Assert.Equal("P-001", new PanelId("P-001").ToString());
    }
}
