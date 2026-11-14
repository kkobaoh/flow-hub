namespace FlowHub.Consumables.Tests;

public class ConsumableIdsTests
{
    [Fact]
    public void 消耗品のIDは同じ値なら等しく空にはできない()
    {
        Assert.Equal(new ConsumableId("C-1"), new ConsumableId("C-1"));
        Assert.Equal("C-1", new ConsumableId("C-1").ToString());
        Assert.Throws<ArgumentException>(() => new ConsumableId(" "));
    }

    [Fact]
    public void 品種のIDは同じ値なら等しく空にはできない()
    {
        Assert.Equal(new VarietyId("V-1"), new VarietyId("V-1"));
        Assert.Equal("V-1", new VarietyId("V-1").ToString());
        Assert.Throws<ArgumentException>(() => new VarietyId(""));
    }
}
