using FlowHub.Routing;
using Microsoft.EntityFrameworkCore;

namespace FlowHub.Traffic.Infrastructure;

/// <summary>
/// 起動するときに交通整理へ登録する AGV（AGV と、その種類の動きの速さ）。
/// </summary>
public sealed record AgvConfiguration(AgvId Id, MotionProfile Motion);

/// <summary>
/// 交通整理のコンテキストの DB の入り口。DbContext は外に出さず、DB の設定を受け取って保存場所を開く。
/// </summary>
public static class TrafficDatabase
{
    /// <summary>交通整理のコンテキストのスキーマ名（SQLite ではファイル名）。</summary>
    public const string Schema = "traffic";

    /// <summary>
    /// 交通整理の保存場所を開く。DB を使える状態にしてから返す。
    /// </summary>
    /// <param name="configure">DB の設定（PostgreSQL か SQLite か、つなぎ先）。</param>
    /// <param name="initialize">DB を使える状態にする（マイグレーションを当てる、テーブルを作る）。</param>
    public static async Task<TrafficStore> OpenAsync(
        Action<DbContextOptionsBuilder> configure,
        Func<DbContext, CancellationToken, Task> initialize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(initialize);

        var builder = new DbContextOptionsBuilder<TrafficDbContext>();
        configure(builder);
        var options = builder.Options;

        await using (var context = new TrafficDbContext(options))
        {
            await initialize(context, cancellationToken).ConfigureAwait(false);
        }

        return new TrafficStore(() => new TrafficDbContext(options));
    }
}

/// <summary>
/// 交通整理の保存場所（DB）。AGV の種類と AGV（構成のデータ）、AGV ごとの次に付けるコマンドの番号とコントロールステータスを持つ。
/// </summary>
public sealed class TrafficStore : ICommandNumberStore, IControlStatusStore
{
    private readonly Func<TrafficDbContext> _createContext;

    internal TrafficStore(Func<TrafficDbContext> createContext) => _createContext = createContext;

    // ---- 構成のデータ ----

    /// <summary>構成のデータ（AGV の種類も AGV も）が、まだ何もないか。</summary>
    public async Task<bool> IsEmptyAsync(CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        return !await context.AgvTypes.AnyAsync(cancellationToken).ConfigureAwait(false)
            && !await context.Agvs.AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>AGV の種類を登録する。</summary>
    public async Task AddAgvTypeAsync(string id, MotionProfile motion, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(motion);

        await using var context = _createContext();
        context.AgvTypes.Add(new AgvTypeRow
        {
            Id = id,
            ForwardSpeedMmPerSecond = motion.ForwardSpeedMmPerSecond,
            BackwardSpeedMmPerSecond = motion.BackwardSpeedMmPerSecond,
            RotationSpeedDegreesPerSecond = motion.RotationSpeedDegreesPerSecond,
        });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>AGV を登録する。種類は、先に登録しておく。コントロールステータスは、切り離しから始める。</summary>
    public async Task AddAgvAsync(AgvId id, string typeId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeId);

        await using var context = _createContext();
        if (await context.AgvTypes.FindAsync([typeId], cancellationToken).ConfigureAwait(false) is null)
        {
            throw new InvalidOperationException($"AGV の種類 {typeId} は登録されていません。");
        }

        context.Agvs.Add(new AgvRow { Id = id.Value, TypeId = typeId, ControlStatus = ControlStatus.Detached });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>登録されている AGV を、種類の動きの速さと一緒に読む（ID の順）。</summary>
    public async Task<IReadOnlyList<AgvConfiguration>> LoadAgvsAsync(CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        var rows = await context.Agvs
            .Join(context.AgvTypes, agv => agv.TypeId, type => type.Id, (agv, type) => new { agv.Id, type.ForwardSpeedMmPerSecond, type.BackwardSpeedMmPerSecond, type.RotationSpeedDegreesPerSecond })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => new AgvConfiguration(new AgvId(r.Id), new MotionProfile(r.ForwardSpeedMmPerSecond, r.BackwardSpeedMmPerSecond, r.RotationSpeedDegreesPerSecond)))
            .ToArray();
    }

    // ---- コントロールステータス ----

    /// <exception cref="InvalidOperationException">DB に登録されていない AGV のとき。</exception>
    public ControlStatus StatusOf(AgvId id)
    {
        using var context = _createContext();
        return FindAgv(context, id).ControlStatus;
    }

    /// <exception cref="InvalidOperationException">DB に登録されていない AGV のとき。</exception>
    public void Save(AgvId id, ControlStatus status)
    {
        using var context = _createContext();
        FindAgv(context, id).ControlStatus = status;
        context.SaveChanges();
    }

    private static AgvRow FindAgv(TrafficDbContext context, AgvId id) =>
        context.Agvs.Find(id.Value) ?? throw new InvalidOperationException($"{id} は DB に登録されていません。");

    // ---- コマンドの番号 ----

    public long NextNumberOf(AgvId id)
    {
        using var context = _createContext();
        return context.CommandNumbers.Find(id.Value)?.NextNumber ?? 1;
    }

    public void Save(AgvId id, long nextNumber)
    {
        using var context = _createContext();
        var row = context.CommandNumbers.Find(id.Value);
        if (row is null)
        {
            context.CommandNumbers.Add(new CommandNumberRow { AgvId = id.Value, NextNumber = nextNumber });
        }
        else
        {
            row.NextNumber = nextNumber;
        }

        context.SaveChanges();
    }
}
