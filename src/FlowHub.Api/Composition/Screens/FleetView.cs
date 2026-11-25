namespace FlowHub.Api.Composition.Screens;

/// <summary>
/// 画面へ送る、AGV と AGV の種類の登録（AGV と AGV の種類の登録の画面）。変わるたびに番号を進める。
/// </summary>
/// <param name="Version">様子の番号。1から始まり、変わるたびに1つ進む。</param>
/// <param name="Types">AGV の種類（ID の順）。</param>
/// <param name="Agvs">登録してある AGV（ID の順。片方にしか登録していないものも）。</param>
/// <param name="Defaults">充電の残量の既定値（種類を加えるときに省いたら、これで埋める）。</param>
/// <param name="Carriers">AGV に付けられるキャリアの種類（加工の種類にあるもの。ID の順）。</param>
internal sealed record FleetSnapshot(long Version, IReadOnlyList<AgvTypeView> Types, IReadOnlyList<FleetAgvView> Agvs, ChargeDefaultsView Defaults, IReadOnlyList<string> Carriers)
{
    public static FleetSnapshot From(long version, FleetScreenState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new FleetSnapshot(version, state.Fleet.Types, state.Agvs, new ChargeDefaultsView(state.Fleet.DefaultChargeAt, state.Fleet.DefaultResumeAt), state.Carriers);
    }

    public bool Equals(FleetSnapshot? other) =>
        other is not null && Version == other.Version && Types.SequenceEqual(other.Types) && Agvs.SequenceEqual(other.Agvs) && Defaults == other.Defaults && Carriers.SequenceEqual(other.Carriers);

    public override int GetHashCode() => HashCode.Combine(Version, Types.Count, Agvs.Count);
}

/// <summary>画面へ送る、登録してある AGV。</summary>
/// <param name="Type">使う種類の ID（配車にだけ登録してあれば null）。</param>
/// <param name="Carrier">キャリアの種類（交通整理にだけ登録してあれば null）。</param>
/// <param name="Control">コントロールステータス（動かしていない AGV なら null）。</param>
/// <param name="Registration"><c>both</c>、<c>trafficOnly</c>、<c>dispatchOnly</c>。</param>
internal sealed record FleetAgvView(string Id, string? Type, string? Carrier, string? Control, string Registration);

internal sealed record ChargeDefaultsView(int ChargeAt, int ResumeAt);

/// <summary>画面へ送る、AGV と AGV の種類の登録のもと（読み比べて、変わったときだけ送る）。</summary>
internal sealed record FleetScreenState(FleetState Fleet, IReadOnlyList<FleetAgvView> Agvs, IReadOnlyList<string> Carriers)
{
    public static readonly FleetScreenState Empty = new(new FleetState([], [], 0, 0), [], []);

    public bool Equals(FleetScreenState? other) =>
        other is not null && Fleet == other.Fleet && Agvs.SequenceEqual(other.Agvs) && Carriers.SequenceEqual(other.Carriers);

    public override int GetHashCode() => HashCode.Combine(Fleet, Agvs.Count, Carriers.Count);
}
