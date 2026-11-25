using FlowHub.Consumables;
using FlowHub.Dispatching;
using FlowHub.ToolPlanning;
using FlowHub.Traffic;

namespace FlowHub.Api.Composition.Screens;

/// <summary>
/// 画面へ送る、すべての消耗品の記録（消耗品の検索の画面と、マップの「消耗品を探す」）。変わるたびに番号を進める。
/// </summary>
/// <param name="Version">様子の番号。1から始まり、変わるたびに1つ進む。</param>
/// <param name="Consumables">消耗品（ID の順）。</param>
internal sealed record ConsumablesSnapshot(long Version, IReadOnlyList<ConsumableSnapshot> Consumables)
{
    public bool Equals(ConsumablesSnapshot? other) => other is not null && Version == other.Version && Consumables.SequenceEqual(other.Consumables);

    public override int GetHashCode() => HashCode.Combine(Version, Consumables.Count);
}

/// <summary>
/// 消耗品から、それが入っている搬送を引くための表（配車の搬送の一覧と、頼んだ側から聞いた中身を、搬送の番号で結び付けたもの）。
/// </summary>
internal sealed class ConsumableTransports
{
    private readonly Dictionary<TransportId, TransportListing> _byTransport;
    private readonly Dictionary<ConsumableId, TransportListing> _byConsumable = [];

    /// <param name="endpointName">受け渡しの相手の参照を、画面に出す名前にする。</param>
    public ConsumableTransports(IReadOnlyList<TransportListing> listings, IReadOnlyDictionary<TransportId, TransportDescription> descriptions, Func<string, string> endpointName)
    {
        ArgumentNullException.ThrowIfNull(listings);
        ArgumentNullException.ThrowIfNull(descriptions);
        EndpointName = endpointName ?? throw new ArgumentNullException(nameof(endpointName));
        _byTransport = listings.ToDictionary(l => l.Transport);
        foreach (var (transport, description) in descriptions)
        {
            if (!_byTransport.TryGetValue(transport, out var listing))
            {
                continue; // 読むあいだに終わった搬送
            }

            foreach (var id in description.CargoIds)
            {
                _byConsumable[id] = listing;
            }
        }
    }

    public Func<string, string> EndpointName { get; }

    /// <summary>その消耗品が入っている搬送。見つからなければ null。</summary>
    public TransportListing? Containing(ConsumableId id) => _byConsumable.GetValueOrDefault(id);

    /// <summary>その搬送を割り当てた AGV。割り当て待ちか、見つからなければ null。</summary>
    public AgvId? AgvOf(TransportId transport) =>
        _byTransport.TryGetValue(transport, out var listing) && listing.Progress is AssignedTo assigned ? assigned.Agv : null;
}

/// <summary>
/// 画面へ送る、1個の消耗品の記録。
/// </summary>
/// <param name="Condition"><c>new</c> か <c>used</c>。品種がわからないものは、記録の状態も確かでないので null。</param>
/// <param name="CheckReason">要確認のわけ（<c>unregisteredId</c>、<c>foundAfterLost</c>、<c>idMismatch</c>）。要確認でなければ null。</param>
/// <param name="Reservation">届ける・回収するの予約。なければ null。</param>
/// <param name="Designation">指名。なければ null。</param>
/// <param name="Shipping">出庫を頼んで、まだ出ていない。</param>
internal sealed record ConsumableSnapshot(
    string Id,
    string? Variety,
    string? ProcessType,
    string? Condition,
    LocationView Location,
    string? CheckReason,
    ReservationView? Reservation,
    DesignationView? Designation,
    bool Shipping)
{
    public static ConsumableSnapshot From(ConsumableRecord record, ConsumableTransports transports)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(transports);
        var reservation = ReservationOf(record, transports);
        return new ConsumableSnapshot(
            record.Id.Value,
            record.Variety?.Value,
            record.ProcessType?.Value,
            ConditionText.Of(record.Variety, record.Condition),
            LocationView.From(record.Location, transports),
            record.Check == NeedsCheckReason.NotNeeded ? null : CheckReasonText.Of(record.Check),
            reservation,
            record.DesignatedFor is { } device ? new DesignationView(device.Value, reservation?.Assigned ?? false) : null,
            record.ShippingRequested);
    }

    private static ReservationView? ReservationOf(ConsumableRecord record, ConsumableTransports transports)
    {
        var (kind, device) = record.Reservation switch
        {
            NoReservation => ((string?)null, (string?)null),
            DeliveryReservation delivery => ("delivery", delivery.Device.Value),
            RecoveryReservation => ("recovery", null),
            _ => throw new ArgumentOutOfRangeException(nameof(record), record.Reservation, "知らない予約です。"),
        };
        if (kind is null)
        {
            return null;
        }

        // 入っている搬送が見つからない（読むあいだに変わった）ときは、番号なしで出す
        return transports.Containing(record.Id) is { } listing
            ? new ReservationView(
                kind,
                device,
                listing.Transport.Value,
                listing.Progress is AssignedTo,
                TransportTexts.SourceName(listing.Source, transports.EndpointName),
                transports.EndpointName(listing.DestinationEndpoint))
            : new ReservationView(kind, device, null, false, null, null);
    }
}

/// <summary>
/// 消耗品の所在。
/// </summary>
/// <param name="Kind"><c>station</c>、<c>slot</c>（ストッカーのスロット）、<c>processing</c>（加工部）、<c>handover</c>（受け渡し部）、<c>agv</c>（AGV の中）、<c>unknown</c>（所在不明）。</param>
/// <param name="Agv">積んでいる AGV。わからなければ null。</param>
internal sealed record LocationView(string Kind, string? Station, string? Device, int? Stocker, int? Slot, string? Agv)
{
    public static LocationView From(ConsumableLocation location, ConsumableTransports transports)
    {
        ArgumentNullException.ThrowIfNull(transports);
        return location switch
        {
            AtStation at => new LocationView("station", at.Station.Value, null, null, null, null),
            InStocker stocker => new LocationView("slot", null, stocker.Device.Value, stocker.Slot.Stocker, stocker.Slot.Slot, null),
            InProcessing processing => new LocationView("processing", null, processing.Device.Value, null, null, null),
            AtTransferUnit unit => new LocationView("handover", null, unit.Device.Value, null, null, null),
            OnAgv onAgv => new LocationView("agv", null, null, null, null, transports.AgvOf(onAgv.Transport)?.Value),
            UnknownLocation => new LocationView("unknown", null, null, null, null, null),
            _ => throw new ArgumentOutOfRangeException(nameof(location), location, "知らない所在です。"),
        };
    }
}

/// <summary>
/// 届ける・回収するの予約。
/// </summary>
/// <param name="Kind"><c>delivery</c>（届ける）か <c>recovery</c>（回収する）。</param>
/// <param name="Device">届け先の装置（届けるときだけ）。</param>
/// <param name="Transport">入っている搬送の番号。見つからなければ null。</param>
/// <param name="Assigned">その搬送に AGV を割り当て済みか。</param>
/// <param name="From">その搬送の搬送元の名前。</param>
/// <param name="To">その搬送の搬送先の名前。</param>
internal sealed record ReservationView(string Kind, string? Device, long? Transport, bool Assigned, string? From, string? To);

/// <summary>指名。</summary>
/// <param name="Assigned">届ける搬送に AGV を割り当て済みか（割り当てたあとは、取り消せない）。</param>
internal sealed record DesignationView(string Device, bool Assigned);

/// <summary>要確認のわけを、画面へ送る文字にする。</summary>
internal static class CheckReasonText
{
    public static string Of(NeedsCheckReason reason) => reason switch
    {
        NeedsCheckReason.UnregisteredId => "unregisteredId",
        NeedsCheckReason.FoundAfterLost => "foundAfterLost",
        NeedsCheckReason.IdMismatch => "idMismatch",
        _ => throw new InvalidOperationException($"要確認のわけではありません（{reason}）。"),
    };
}
