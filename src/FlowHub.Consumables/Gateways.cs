using FlowHub.Dispatching;

namespace FlowHub.Consumables;

/// <summary>
/// 受け渡しで出し入れする消耗品と、ストッカーのスロット（装置で降ろすときは入れ先、回収するときは取り出し元）。
/// </summary>
public sealed record CargoSlot(ConsumableId Consumable, SlotRef Slot);

/// <summary>
/// 装置とのやり取りのポート。やり取りの方式は未定（未決事項）。
/// </summary>
public interface IDeviceGateway
{
    /// <summary>装置に、受け渡し（キャリアへの出し入れ）の許可を出す。終わったら、出し入れした消耗品の ID を付けて完了を通知してくる。</summary>
    Task GrantTransferAsync(DeviceId device, TransportId transport, TransferSide side, IReadOnlyList<CargoSlot> cargo, CancellationToken cancellationToken);
}

/// <summary>
/// 上位システムに補充を頼むポート。上位システムがない構成では、ユーザーに知らせる実装に差し替える。
/// </summary>
public interface IReplenishmentGateway
{
    /// <summary>ステーションへの補充を頼む。</summary>
    /// <param name="quantity">足りない数。</param>
    Task RequestReplenishmentAsync(VarietyId variety, int quantity, CancellationToken cancellationToken);
}

/// <summary>
/// ステーションとのやり取りのポート。やり取りの方式は未定（未決事項）。
/// </summary>
public interface IStationGateway
{
    /// <summary>ステーションに、受け渡しの許可を出す。終わったら、出し入れした消耗品の ID を付けて完了を通知してくる。</summary>
    Task GrantTransferAsync(StationId station, TransportId transport, TransferSide side, IReadOnlyList<ConsumableId> cargo, CancellationToken cancellationToken);
}
