using FlowHub.Dispatching;
using FlowHub.Map;
using Microsoft.EntityFrameworkCore;

namespace FlowHub.Consumables.Infrastructure;

/// <summary>
/// 消耗品の管理のコンテキストの DB の入り口。DbContext は外に出さず、DB の設定を受け取って保存場所を開く。
/// </summary>
public static class ConsumablesDatabase
{
    /// <summary>消耗品の管理のコンテキストのスキーマ名（SQLite ではファイル名）。</summary>
    public const string Schema = "consumables";

    /// <summary>
    /// 消耗品の管理の保存場所を開く。DB を使える状態にしてから返す。
    /// </summary>
    /// <param name="configure">DB の設定（PostgreSQL か SQLite か、つなぎ先）。</param>
    /// <param name="initialize">DB を使える状態にする（マイグレーションを当てる、テーブルを作る）。</param>
    public static async Task<ConsumablesStore> OpenAsync(
        Action<DbContextOptionsBuilder> configure,
        Func<DbContext, CancellationToken, Task> initialize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(initialize);

        var builder = new DbContextOptionsBuilder<ConsumablesDbContext>();
        configure(builder);
        var options = builder.Options;

        await using (var context = new ConsumablesDbContext(options))
        {
            await initialize(context, cancellationToken).ConfigureAwait(false);
        }

        return new ConsumablesStore(() => new ConsumablesDbContext(options));
    }
}

/// <summary>
/// 消耗品の管理の保存場所（DB）。装置、ステーション、加工の種類、品種（構成のデータ）と、
/// 実行中に変わるストッカーの設定と入庫の予定を持つ。
/// </summary>
public sealed class ConsumablesStore : IConsumablesPersistence
{
    private readonly Func<ConsumablesDbContext> _createContext;

    internal ConsumablesStore(Func<ConsumablesDbContext> createContext) => _createContext = createContext;

    // ---- 構成のデータ ----

    /// <summary>構成のデータが、まだ何もないか。</summary>
    public async Task<bool> IsEmptyAsync(CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        return !await context.Devices.AnyAsync(cancellationToken).ConfigureAwait(false)
            && !await context.Stations.AnyAsync(cancellationToken).ConfigureAwait(false)
            && !await context.ProcessTypes.AnyAsync(cancellationToken).ConfigureAwait(false)
            && !await context.Varieties.AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>加工の種類を登録する。</summary>
    public async Task AddProcessTypeAsync(ProcessTypeSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await using var context = _createContext();
        context.ProcessTypes.Add(new ProcessTypeRow { Id = settings.Id.Value, CarrierType = settings.Carrier.Value, CarrierCapacity = settings.CarrierCapacity });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>品種を登録する。加工の種類は、先に登録しておく。</summary>
    public async Task AddVarietyAsync(VarietyId variety, ProcessTypeId processType, CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        if (await context.ProcessTypes.FindAsync([processType.Value], cancellationToken).ConfigureAwait(false) is null)
        {
            throw new InvalidOperationException($"加工の種類 {processType} は登録されていません。");
        }

        context.Varieties.Add(new VarietyRow { Id = variety.Value, ProcessTypeId = processType.Value });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>装置を登録する。</summary>
    public async Task AddDeviceAsync(DeviceLayout layout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layout);

        await using var context = _createContext();
        context.Devices.Add(new DeviceRow { Id = layout.Id.Value, TransferPanel = layout.TransferPanel.Value });
        context.DeviceStockers.AddRange(layout.SlotsPerStocker.Select((slots, index) => new DeviceStockerRow
        {
            DeviceId = layout.Id.Value,
            StockerNumber = index + 1,
            SlotCount = slots,
        }));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>ステーションを登録する。</summary>
    public async Task AddStationAsync(StationLayout layout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layout);

        await using var context = _createContext();
        context.Stations.Add(new StationRow { Id = layout.Id.Value, TransferPanel = layout.TransferPanel.Value });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 構成のデータを読む（起動したとき）。ID の順に並べる。
    /// </summary>
    public async Task<ConsumablesConfiguration> LoadAsync(CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        var devices = await context.Devices.ToListAsync(cancellationToken).ConfigureAwait(false);
        var stockers = await context.DeviceStockers.ToListAsync(cancellationToken).ConfigureAwait(false);
        var stations = await context.Stations.ToListAsync(cancellationToken).ConfigureAwait(false);
        var processTypes = await context.ProcessTypes.ToListAsync(cancellationToken).ConfigureAwait(false);
        var varieties = await context.Varieties.ToListAsync(cancellationToken).ConfigureAwait(false);
        var settings = await context.StockerSettings.ToListAsync(cancellationToken).ConfigureAwait(false);
        var arrivals = await context.ExpectedArrivals.ToListAsync(cancellationToken).ConfigureAwait(false);

        return new ConsumablesConfiguration(
            Devices: devices
                .OrderBy(d => d.Id, StringComparer.Ordinal)
                .Select(d => new DeviceLayout(
                    new DeviceId(d.Id),
                    new PanelId(d.TransferPanel),
                    stockers.Where(s => s.DeviceId == d.Id).OrderBy(s => s.StockerNumber).Select(s => s.SlotCount).ToArray()))
                .ToArray(),
            Stations: stations
                .OrderBy(s => s.Id, StringComparer.Ordinal)
                .Select(s => new StationLayout(new StationId(s.Id), new PanelId(s.TransferPanel)))
                .ToArray(),
            ProcessTypes: processTypes
                .OrderBy(p => p.Id, StringComparer.Ordinal)
                .Select(p => new ProcessTypeSettings(new ProcessTypeId(p.Id), new CarrierTypeId(p.CarrierType), p.CarrierCapacity))
                .ToArray(),
            Varieties: varieties
                .OrderBy(v => v.Id, StringComparer.Ordinal)
                .Select(v => new VarietyConfiguration(new VarietyId(v.Id), new ProcessTypeId(v.ProcessTypeId)))
                .ToArray(),
            StockerSettings: devices
                .Where(d => d.HasStockerSetting)
                .OrderBy(d => d.Id, StringComparer.Ordinal)
                .Select(d => new StockerSettingConfiguration(
                    new DeviceId(d.Id),
                    settings.Where(s => s.DeviceId == d.Id).ToDictionary(s => new VarietyId(s.VarietyId), s => s.Count)))
                .ToArray(),
            ExpectedArrivals: arrivals
                .OrderBy(a => a.ConsumableId, StringComparer.Ordinal)
                .Select(a => new ExpectedArrival(new ConsumableId(a.ConsumableId), new VarietyId(a.VarietyId)))
                .ToArray());
    }

    // ---- 実行中に変わる構成のデータ ----

    public void SaveStockerSetting(DeviceId device, IReadOnlyDictionary<VarietyId, int> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        using var context = _createContext();
        var row = context.Devices.Find(device.Value) ?? throw new InvalidOperationException($"装置 {device} は登録されていません。");
        row.HasStockerSetting = true;
        context.StockerSettings.RemoveRange(context.StockerSettings.Where(s => s.DeviceId == device.Value));
        context.StockerSettings.AddRange(counts.Select(pair => new StockerSettingRow { DeviceId = device.Value, VarietyId = pair.Key.Value, Count = pair.Value }));
        context.SaveChanges();
    }

    public void AddExpectedArrival(ConsumableId consumable, VarietyId variety)
    {
        using var context = _createContext();
        context.ExpectedArrivals.Add(new ExpectedArrivalRow { ConsumableId = consumable.Value, VarietyId = variety.Value });
        context.SaveChanges();
    }

    public void RemoveExpectedArrival(ConsumableId consumable)
    {
        using var context = _createContext();
        if (context.ExpectedArrivals.Find(consumable.Value) is { } row)
        {
            context.ExpectedArrivals.Remove(row);
            context.SaveChanges();
        }
    }

    public void SaveProcessType(ProcessTypeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        using var context = _createContext();
        var row = context.ProcessTypes.Find(settings.Id.Value);
        if (row is null)
        {
            row = new ProcessTypeRow { Id = settings.Id.Value };
            context.ProcessTypes.Add(row);
        }

        row.CarrierType = settings.Carrier.Value;
        row.CarrierCapacity = settings.CarrierCapacity;
        context.SaveChanges();
    }

    public void DeleteProcessType(ProcessTypeId processType)
    {
        using var context = _createContext();
        if (context.ProcessTypes.Find(processType.Value) is { } row)
        {
            context.ProcessTypes.Remove(row);
            context.SaveChanges();
        }
    }

    public void SaveVariety(VarietyId variety, ProcessTypeId processType)
    {
        using var context = _createContext();
        var row = context.Varieties.Find(variety.Value);
        if (row is null)
        {
            context.Varieties.Add(new VarietyRow { Id = variety.Value, ProcessTypeId = processType.Value });
        }
        else
        {
            row.ProcessTypeId = processType.Value;
        }

        context.SaveChanges();
    }

    public void DeleteVariety(VarietyId variety)
    {
        using var context = _createContext();
        using var transaction = context.Database.BeginTransaction();
        context.ExpectedArrivals.RemoveRange(context.ExpectedArrivals.Where(a => a.VarietyId == variety.Value));
        context.StockerSettings.RemoveRange(context.StockerSettings.Where(s => s.VarietyId == variety.Value));
        context.SaveChanges();
        if (context.Varieties.Find(variety.Value) is { } row)
        {
            context.Varieties.Remove(row);
            context.SaveChanges();
        }

        transaction.Commit();
    }
}
