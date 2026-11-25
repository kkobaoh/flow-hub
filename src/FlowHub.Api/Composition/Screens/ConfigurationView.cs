using FlowHub.Consumables;

namespace FlowHub.Api.Composition.Screens;

/// <summary>
/// 画面へ送る、消耗品の構成（消耗品の構成の画面の、加工の種類と品種）。変わるたびに番号を進める。
/// </summary>
/// <param name="Version">様子の番号。1から始まり、変わるたびに1つ進む。</param>
/// <param name="ProcessTypes">加工の種類（ID の順）。</param>
/// <param name="Varieties">品種（ID の順）。</param>
internal sealed record ConfigurationSnapshot(long Version, IReadOnlyList<ProcessTypeConfigurationView> ProcessTypes, IReadOnlyList<VarietyView> Varieties)
{
    public static ConfigurationSnapshot From(long version, ConsumablesConfigurationView configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new ConfigurationSnapshot(
            version,
            [.. configuration.ProcessTypes.Select(p => new ProcessTypeConfigurationView(p.Id.Value, p.Carrier.Value, p.CarrierCapacity))],
            [.. configuration.Varieties.Select(v => new VarietyView(
                v.Variety.Value,
                v.ProcessType.Value,
                [.. v.ExpectedIds.Select(id => id.Value)],
                [.. v.StockedIds.Select(id => id.Value)],
                v.InUse))]);
    }

    public bool Equals(ConfigurationSnapshot? other) =>
        other is not null && Version == other.Version && ProcessTypes.SequenceEqual(other.ProcessTypes) && Varieties.SequenceEqual(other.Varieties);

    public override int GetHashCode() => HashCode.Combine(Version, ProcessTypes.Count, Varieties.Count);
}

/// <summary>画面へ送る、加工の種類。</summary>
/// <param name="Carrier">キャリアの種類。</param>
/// <param name="Capacity">キャリアに入る消耗品の個数。</param>
internal sealed record ProcessTypeConfigurationView(string Id, string Carrier, int Capacity);

/// <summary>画面へ送る、品種。</summary>
/// <param name="Expected">入庫の予定の ID（外せる。ID の順）。</param>
/// <param name="Stocked">消耗品の記録にある ID（外せない。ID の順）。</param>
/// <param name="InUse">使われているか（使われていたら、加工の種類を変えられず、消せない）。</param>
internal sealed record VarietyView(string Id, string ProcessType, IReadOnlyList<string> Expected, IReadOnlyList<string> Stocked, bool InUse)
{
    public bool Equals(VarietyView? other) =>
        other is not null
        && Id == other.Id
        && ProcessType == other.ProcessType
        && Expected.SequenceEqual(other.Expected)
        && Stocked.SequenceEqual(other.Stocked)
        && InUse == other.InUse;

    public override int GetHashCode() => HashCode.Combine(Id, ProcessType, InUse);
}
