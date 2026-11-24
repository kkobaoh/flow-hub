using FlowHub.Traffic;
using Microsoft.EntityFrameworkCore;

namespace FlowHub.Dispatching.Infrastructure;

/// <summary>
/// 起動するときに配車へ登録する AGV（キャリアの種類と、充電の設定値）。
/// </summary>
public sealed record DispatchingAgvConfiguration(AgvId Id, CarrierTypeId Carrier, int GoChargeBelowPercent, int ResumeAtPercent);

/// <summary>
/// 配車のコンテキストの DB の入り口。DbContext は外に出さず、DB の設定を受け取って保存場所を開く。
/// </summary>
public static class DispatchingDatabase
{
    /// <summary>配車のコンテキストのスキーマ名（SQLite ではファイル名）。</summary>
    public const string Schema = "dispatching";

    /// <summary>
    /// 配車の保存場所を開く。DB を使える状態にしてから返す。
    /// </summary>
    /// <param name="configure">DB の設定（PostgreSQL か SQLite か、つなぎ先）。</param>
    /// <param name="initialize">DB を使える状態にする（マイグレーションを当てる、テーブルを作る）。</param>
    public static async Task<DispatchingStore> OpenAsync(
        Action<DbContextOptionsBuilder> configure,
        Func<DbContext, CancellationToken, Task> initialize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(initialize);

        var builder = new DbContextOptionsBuilder<DispatchingDbContext>();
        configure(builder);
        var options = builder.Options;

        await using (var context = new DispatchingDbContext(options))
        {
            await initialize(context, cancellationToken).ConfigureAwait(false);
        }

        return new DispatchingStore(() => new DispatchingDbContext(options));
    }
}

/// <summary>
/// 配車の保存場所（DB）。AGV の種類ごとの充電の設定値、システム全体の充電の既定値、AGV ごとのキャリアの種類を持つ。
/// </summary>
/// <remarks>
/// 既定値は、AGV の種類を登録するときに、省略された値を埋めて保存するのに使う。保存する値はいつも具体的な数なので、
/// 保存するときに「割り当てに戻す値は、充電に向かう値以上」を確かめられる。あとで既定値を変えても、登録済みの種類の値は変わらない。
/// </remarks>
public sealed class DispatchingStore
{
    /// <summary>既定値を設定していないときの、充電に向かう値。</summary>
    public const int InitialGoChargeBelowPercent = 20;

    /// <summary>既定値を設定していないときの、割り当てに戻す値。</summary>
    public const int InitialResumeAtPercent = 80;

    private readonly Func<DispatchingDbContext> _createContext;

    internal DispatchingStore(Func<DispatchingDbContext> createContext) => _createContext = createContext;

    /// <summary>構成のデータ（AGV の種類も AGV も）が、まだ何もないか。</summary>
    public async Task<bool> IsEmptyAsync(CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        return !await context.AgvTypes.AnyAsync(cancellationToken).ConfigureAwait(false)
            && !await context.Agvs.AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>システム全体の充電の既定値を設定する（設定し直せる。登録済みの種類の値は変わらない）。</summary>
    public async Task SetChargingDefaultsAsync(int goChargeBelowPercent, int resumeAtPercent, CancellationToken cancellationToken)
    {
        EnsureValid(goChargeBelowPercent, resumeAtPercent);

        await using var context = _createContext();
        var row = await context.ChargingDefaults.FindAsync([ChargingDefaultsRow.SingleId], cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            context.ChargingDefaults.Add(new ChargingDefaultsRow { GoChargeBelowPercent = goChargeBelowPercent, ResumeAtPercent = resumeAtPercent });
        }
        else
        {
            row.GoChargeBelowPercent = goChargeBelowPercent;
            row.ResumeAtPercent = resumeAtPercent;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// AGV の種類を登録する。充電の設定値は省略でき、省略したら、そのときの既定値（設定していなければ 20% と 80%）で埋めて保存する。
    /// </summary>
    public async Task AddAgvTypeAsync(string id, int? goChargeBelowPercent, int? resumeAtPercent, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (goChargeBelowPercent is { } goBelow)
        {
            EnsurePercent(goBelow, nameof(goChargeBelowPercent));
        }

        if (resumeAtPercent is { } resumeAt)
        {
            EnsurePercent(resumeAt, nameof(resumeAtPercent));
        }

        await using var context = _createContext();
        var defaults = await context.ChargingDefaults.FindAsync([ChargingDefaultsRow.SingleId], cancellationToken).ConfigureAwait(false);
        var filledGoBelow = goChargeBelowPercent ?? defaults?.GoChargeBelowPercent ?? InitialGoChargeBelowPercent;
        var filledResumeAt = resumeAtPercent ?? defaults?.ResumeAtPercent ?? InitialResumeAtPercent;
        EnsureValid(filledGoBelow, filledResumeAt);

        context.AgvTypes.Add(new DispatchingAgvTypeRow { Id = id, GoChargeBelowPercent = filledGoBelow, ResumeAtPercent = filledResumeAt });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>AGV を登録する。種類は、先に登録しておく。</summary>
    public async Task AddAgvAsync(AgvId id, string typeId, CarrierTypeId carrier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeId);

        await using var context = _createContext();
        if (await context.AgvTypes.FindAsync([typeId], cancellationToken).ConfigureAwait(false) is null)
        {
            throw new InvalidOperationException($"AGV の種類 {typeId} は登録されていません。");
        }

        context.Agvs.Add(new DispatchingAgvRow { Id = id.Value, TypeId = typeId, CarrierType = carrier.Value });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 登録されている AGV を読む（ID の順）。
    /// </summary>
    public async Task<IReadOnlyList<DispatchingAgvConfiguration>> LoadAgvsAsync(CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        var rows = await context.Agvs
            .Join(context.AgvTypes, agv => agv.TypeId, type => type.Id, (agv, type) => new { agv.Id, agv.CarrierType, type.GoChargeBelowPercent, type.ResumeAtPercent })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => new DispatchingAgvConfiguration(new AgvId(r.Id), new CarrierTypeId(r.CarrierType), r.GoChargeBelowPercent, r.ResumeAtPercent))
            .ToArray();
    }

    /// <summary>
    /// 充電の設定値の決まり：どちらも 0〜100%、割り当てに戻す値は充電に向かう値以上（同じ値はよい）。
    /// </summary>
    private static void EnsureValid(int goChargeBelowPercent, int resumeAtPercent)
    {
        EnsurePercent(goChargeBelowPercent, nameof(goChargeBelowPercent));
        EnsurePercent(resumeAtPercent, nameof(resumeAtPercent));
        if (resumeAtPercent < goChargeBelowPercent)
        {
            throw new ArgumentException(
                $"割り当てに戻す値（{resumeAtPercent}%）が、充電に向かう値（{goChargeBelowPercent}%）より小さくなっています。",
                nameof(resumeAtPercent));
        }
    }

    private static void EnsurePercent(int percent, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(percent, name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100, name);
    }
}
