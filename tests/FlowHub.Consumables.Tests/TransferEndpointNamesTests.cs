using FlowHub.Consumables.Domain;

namespace FlowHub.Consumables.Tests;

/// <summary>
/// 受け渡しの相手の参照を、画面に出す名前にするテスト。
/// </summary>
public class TransferEndpointNamesTests
{
    [Fact]
    public void 装置とステーションの参照はその番号にする()
    {
        Assert.Equal("DEV-1", TransferEndpointNames.DisplayName(Endpoints.Device(new DeviceId("DEV-1"))));
        Assert.Equal("ST-1", TransferEndpointNames.DisplayName(Endpoints.Station(new StationId("ST-1"))));
    }

    [Fact]
    public void 読めない参照はそのまま出す()
    {
        Assert.Equal("somewhere", TransferEndpointNames.DisplayName("somewhere"));
    }
}
