using FlowHub.Routing;
using Microsoft.EntityFrameworkCore;

namespace FlowHub.Traffic.Infrastructure;

/// <summary>
/// 起動するときに交通整理へ登録する AGV（AGV と、その種類の動きの速さ）。
/// </summary>
/// <param name="CanTravelSpecialTransfer">その種類が、特殊消耗品受け渡しパネルを走れるか。</param>
public sealed record AgvConfiguration(AgvId Id, MotionProfile Motion, bool CanTravelSpecialTransfer = false);

/// <summary>
/// 交通整理が持つ、AGV の種類の設定。
/// </summary>
/// <param name="Id">種類の ID（AGV が使う種類を指す。あとから変えない）。</param>
/// <param name="Name">画面に出す名前（いつでも変えられる。ほかの種類と同じ名前は付けられない）。</param>
/// <param name="CanTravelSpecialTransfer">特殊消耗品受け渡しパネルを走れるか。</param>
public sealed record AgvTypeSettings(string Id, string Name, MotionProfile Motion, bool CanTravelSpecialTransfer);

/// <summary>AGV の種類を保存した結果。断ったときは、何も変えていない。</summary>
public enum AgvTypeSaveResult
{
    Saved,

    /// <summary>その ID の種類は、もうある（加えるとき）。</summary>
    IdTaken,

    /// <summary>その名前の種類は、もうある。</summary>
    NameTaken,

    /// <summary>その ID の種類はない（変えるとき）。</summary>
    NotFound,
}

/// <summary>AGV の種類を消した結果。断ったときは、何も変えていない。</summary>
public enum AgvTypeDeleteResult
{
    Deleted,

    NotFound,

    /// <summary>その種類を使う AGV がある。</summary>
    InUse,
}

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

    /// <summary>AGV の種類を登録する。特殊消耗品受け渡しパネルは、走れないとする。</summary>
    public Task AddAgvTypeAsync(string id, MotionProfile motion, CancellationToken cancellationToken) =>
        AddAgvTypeAsync(id, motion, canTravelSpecialTransfer: false, cancellationToken);

    /// <summary>AGV の種類を登録する（名前は ID と同じにする。初期データから入れるとき）。</summary>
    /// <param name="canTravelSpecialTransfer">特殊消耗品受け渡しパネルを走れるか。</param>
    /// <exception cref="InvalidOperationException">同じ ID か同じ名前の種類が、もうあるとき。</exception>
    public async Task AddAgvTypeAsync(string id, MotionProfile motion, bool canTravelSpecialTransfer, CancellationToken cancellationToken)
    {
        var result = await AddAgvTypeAsync(new AgvTypeSettings(id, id, motion, canTravelSpecialTransfer), cancellationToken).ConfigureAwait(false);
        if (result != AgvTypeSaveResult.Saved)
        {
            throw new InvalidOperationException($"AGV の種類 {id} を登録できません（{result}）。");
        }
    }

    /// <summary>AGV の種類を加える。同じ ID か同じ名前の種類があれば、断る。</summary>
    public async Task<AgvTypeSaveResult> AddAgvTypeAsync(AgvTypeSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Name);

        await using var context = _createContext();
        if (await context.AgvTypes.AnyAsync(t => t.Id == settings.Id, cancellationToken).ConfigureAwait(false))
        {
            return AgvTypeSaveResult.IdTaken;
        }

        if (await context.AgvTypes.AnyAsync(t => t.Name == settings.Name, cancellationToken).ConfigureAwait(false))
        {
            return AgvTypeSaveResult.NameTaken;
        }

        var row = new AgvTypeRow { Id = settings.Id };
        Apply(row, settings);
        context.AgvTypes.Add(row);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return AgvTypeSaveResult.Saved;
    }

    /// <summary>AGV の種類の名前と値を変える（ID は変えない）。ほかの種類と同じ名前なら、断る。</summary>
    public async Task<AgvTypeSaveResult> UpdateAgvTypeAsync(AgvTypeSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Name);

        await using var context = _createContext();
        if (await context.AgvTypes.FindAsync([settings.Id], cancellationToken).ConfigureAwait(false) is not { } row)
        {
            return AgvTypeSaveResult.NotFound;
        }

        if (await context.AgvTypes.AnyAsync(t => t.Name == settings.Name && t.Id != settings.Id, cancellationToken).ConfigureAwait(false))
        {
            return AgvTypeSaveResult.NameTaken;
        }

        Apply(row, settings);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return AgvTypeSaveResult.Saved;
    }

    /// <summary>AGV の種類を消す。使う AGV があれば、断る。</summary>
    public async Task<AgvTypeDeleteResult> DeleteAgvTypeAsync(string id, CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        if (await context.AgvTypes.FindAsync([id], cancellationToken).ConfigureAwait(false) is not { } row)
        {
            return AgvTypeDeleteResult.NotFound;
        }

        if (await context.Agvs.AnyAsync(a => a.TypeId == id, cancellationToken).ConfigureAwait(false))
        {
            return AgvTypeDeleteResult.InUse;
        }

        context.AgvTypes.Remove(row);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return AgvTypeDeleteResult.Deleted;
    }

    /// <summary>AGV の種類（ID の順）。</summary>
    public async Task<IReadOnlyList<AgvTypeSettings>> LoadAgvTypesAsync(CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        var rows = await context.AgvTypes.ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => new AgvTypeSettings(r.Id, r.Name, new MotionProfile(r.ForwardSpeedMmPerSecond, r.BackwardSpeedMmPerSecond, r.RotationSpeedDegreesPerSecond), r.CanTravelSpecialTransfer))
            .ToArray();
    }

    /// <summary>その種類を使う AGV（ID の順）。</summary>
    public async Task<IReadOnlyList<AgvId>> AgvsOfTypeAsync(string typeId, CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        var ids = await context.Agvs.Where(a => a.TypeId == typeId).Select(a => a.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. ids.Order(StringComparer.Ordinal).Select(id => new AgvId(id))];
    }

    private static void Apply(AgvTypeRow row, AgvTypeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings.Motion);
        row.Name = settings.Name;
        row.ForwardSpeedMmPerSecond = settings.Motion.ForwardSpeedMmPerSecond;
        row.BackwardSpeedMmPerSecond = settings.Motion.BackwardSpeedMmPerSecond;
        row.RotationSpeedDegreesPerSecond = settings.Motion.RotationSpeedDegreesPerSecond;
        row.CanTravelSpecialTransfer = settings.CanTravelSpecialTransfer;
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
            .Join(context.AgvTypes, agv => agv.TypeId, type => type.Id, (agv, type) => new { agv.Id, type.ForwardSpeedMmPerSecond, type.BackwardSpeedMmPerSecond, type.RotationSpeedDegreesPerSecond, type.CanTravelSpecialTransfer })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => new AgvConfiguration(new AgvId(r.Id), new MotionProfile(r.ForwardSpeedMmPerSecond, r.BackwardSpeedMmPerSecond, r.RotationSpeedDegreesPerSecond), r.CanTravelSpecialTransfer))
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
