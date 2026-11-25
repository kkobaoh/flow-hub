using FlowHub.Dispatching;
using FlowHub.ToolPlanning;

namespace FlowHub.Api.Composition.Screens;

/// <summary>
/// 画面へ送る搬送の一覧（待ち行列と、AGV に割り当てているもの）。変わるたびに番号を進める。
/// </summary>
/// <param name="Version">様子の番号。1から始まり、変わるたびに1つ進む。</param>
/// <param name="Transports">搬送（番号の順）。</param>
internal sealed record TransportsSnapshot(long Version, IReadOnlyList<TransportSnapshot> Transports)
{
    public bool Equals(TransportsSnapshot? other) => other is not null && Version == other.Version && Transports.SequenceEqual(other.Transports);

    public override int GetHashCode() => HashCode.Combine(Version, Transports.Count);
}

/// <summary>
/// 画面へ送る、1つの搬送。
/// </summary>
/// <param name="Id">搬送の番号。</param>
/// <param name="Source">搬送元の名前（受け渡しの相手。ものを積んだまま止まっていた AGV 自身なら、その AGV）。</param>
/// <param name="Destination">搬送先の名前（受け渡しの相手）。</param>
/// <param name="Purpose">目的（頼んだ側から聞いたもの）。<c>delivery</c>、<c>recovery</c>、<c>deliveryFromAgv</c>、<c>returnFromAgv</c>。聞けなければ null。</param>
/// <param name="Cargo">運ぶものの、品種ごとの数（品種がわからないものは null）。頼んだ側から聞けなければ、品種なしの数だけ。</param>
/// <param name="Stage">段階。<c>waiting</c>（AGV の割り当て待ち）と、AGV の様子の搬送と同じ段階（<c>movingToSource</c> … <c>interrupted</c>）。</param>
/// <param name="Agv">割り当てた AGV。割り当て待ちなら null。</param>
/// <param name="Paused">一時停止しているか。</param>
internal sealed record TransportSnapshot(long Id, string Source, string Destination, DateTimeOffset NeededBy, string? Purpose, IReadOnlyList<CargoCountView> Cargo, string Stage, string? Agv, bool Paused)
{
    /// <summary>配車の搬送と、頼んだ側から聞いた中身を、画面へ送る形にする。</summary>
    /// <param name="description">頼んだ側から聞いた中身。聞けなければ null。</param>
    /// <param name="endpointName">受け渡しの相手の参照を、画面に出す名前にする。</param>
    public static TransportSnapshot From(TransportListing listing, TransportDescription? description, Func<string, string> endpointName)
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(endpointName);
        var (stage, agv, paused) = listing.Progress switch
        {
            WaitingForAgv => ("waiting", (string?)null, false),
            AssignedTo assigned => (TransportTexts.Stage(assigned.Stage), assigned.Agv.Value, assigned.Paused),
            _ => throw new ArgumentOutOfRangeException(nameof(listing), listing.Progress, "知らない搬送の進み具合です。"),
        };
        IReadOnlyList<CargoCountView> cargo = description is null
            ? [new CargoCountView(null, listing.CargoRefs.Count)]
            : [.. description.Cargo.Select(c => new CargoCountView(c.Variety?.Value, c.Count))];
        return new TransportSnapshot(
            listing.Transport.Value,
            TransportTexts.SourceName(listing.Source, endpointName),
            endpointName(listing.DestinationEndpoint),
            listing.NeededBy,
            description is null ? null : PurposeText(description.Purpose),
            cargo,
            stage,
            agv,
            paused);
    }

    public bool Equals(TransportSnapshot? other) =>
        other is not null
        && Id == other.Id
        && Source == other.Source
        && Destination == other.Destination
        && NeededBy == other.NeededBy
        && Purpose == other.Purpose
        && Cargo.SequenceEqual(other.Cargo)
        && Stage == other.Stage
        && Agv == other.Agv
        && Paused == other.Paused;

    public override int GetHashCode() => HashCode.Combine(Id, Stage, Agv, Paused);

    private static string PurposeText(TransportPurpose purpose) => purpose switch
    {
        TransportPurpose.Delivery => "delivery",
        TransportPurpose.Recovery => "recovery",
        TransportPurpose.DeliveryFromAgv => "deliveryFromAgv",
        TransportPurpose.ReturnFromAgv => "returnFromAgv",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "知らない搬送の目的です。"),
    };
}

/// <summary>運ぶものの、品種ごとの数。</summary>
/// <param name="Variety">品種。わからなければ null。</param>
internal sealed record CargoCountView(string? Variety, int Count);

/// <summary>搬送を画面へ送るときの言葉（AGV の様子の搬送と、搬送の一覧で同じにする）。</summary>
internal static class TransportTexts
{
    /// <summary>搬送元の名前（受け渡しの相手か、ものを積んだまま止まっていた AGV 自身）。</summary>
    public static string SourceName(AssignmentSource source, Func<string, string> endpointName) => source switch
    {
        SourceTransferPoint point => endpointName(point.Endpoint),
        SourceAgv agv => agv.Agv.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "知らない搬送元です。"),
    };

    public static string Stage(AssignedTransportStage stage) => stage switch
    {
        AssignedTransportStage.MovingToSource => "movingToSource",
        AssignedTransportStage.LoweringAtSource => "loweringAtSource",
        AssignedTransportStage.TransferringAtSource => "transferringAtSource",
        AssignedTransportStage.RaisingAtSource => "raisingAtSource",
        AssignedTransportStage.MovingToDestination => "movingToDestination",
        AssignedTransportStage.LoweringAtDestination => "loweringAtDestination",
        AssignedTransportStage.TransferringAtDestination => "transferringAtDestination",
        AssignedTransportStage.RaisingAtDestination => "raisingAtDestination",
        AssignedTransportStage.Interrupted => "interrupted",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "知らない搬送の段階です。"),
    };
}
