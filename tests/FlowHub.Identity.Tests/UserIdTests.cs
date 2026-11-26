using FlowHub.Identity.Domain;

namespace FlowHub.Identity.Tests;

public class UserIdTests
{
    [Theory]
    [InlineData("setup")]
    [InlineData("　setup ")]
    [InlineData("")]
    [InlineData("　 ")]
    public void 空または初期設定専用のIDは登録できない(string input)
    {
        Assert.Throws<ArgumentException>(() => new UserId(input));
    }

    [Fact]
    public void 前後の空白を除き大文字と小文字を区別する()
    {
        Assert.Equal(new UserId("Setup"), new UserId("  Setup　"));
        Assert.NotEqual(new UserId("Setup"), new UserId("SETUP"));
    }
}
