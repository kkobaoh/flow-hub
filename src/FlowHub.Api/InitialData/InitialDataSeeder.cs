using System.Text.Json;
using System.Text.Json.Serialization;
using FlowHub.Consumables;
using FlowHub.Consumables.Infrastructure;
using FlowHub.Dispatching;
using FlowHub.Dispatching.Infrastructure;
using FlowHub.Map;
using FlowHub.Map.Infrastructure;
using FlowHub.Routing;
using FlowHub.Traffic;
using FlowHub.Traffic.Infrastructure;
using Microsoft.Extensions.Logging;

namespace FlowHub.Api.InitialData;

/// <summary>
/// 開発用の初期データを読めないか、中身が正しくない。起動を止める。
/// </summary>
public sealed class InitialDataException : Exception
{
    public InitialDataException()
    {
    }

    public InitialDataException(string message)
        : base(message)
    {
    }

    public InitialDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// 開発用の初期データ（JSON ファイル）を DB に入れる（docs/plan.md「初期データ」）。
/// マップ、交通整理、配車、消耗品の管理の4つの DB がすべて空のときだけ、まとめて入れる。
/// 入れる前に全体を確かめ、ファイルがないか中身が正しくなければ、何も入れずにログに出して <see cref="InitialDataException"/> を投げる。
/// </summary>
internal static partial class InitialDataSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>4つの DB がすべて空なら、初期データを入れる。入れたら true。</summary>
    public static async Task<bool> SeedIfEmptyAsync(
        string path,
        DbMapStore map,
        TrafficStore traffic,
        DispatchingStore dispatching,
        ConsumablesStore consumables,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(traffic);
        ArgumentNullException.ThrowIfNull(dispatching);
        ArgumentNullException.ThrowIfNull(consumables);

        var empty = map.Current is null
            && await traffic.IsEmptyAsync(cancellationToken).ConfigureAwait(false)
            && await dispatching.IsEmptyAsync(cancellationToken).ConfigureAwait(false)
            && await consumables.IsEmptyAsync(cancellationToken).ConfigureAwait(false);
        if (!empty)
        {
            return false;
        }

        var data = Prepare(path, logger);

        map.Publish(data.Map);
        foreach (var type in data.AgvTypes)
        {
            await traffic.AddAgvTypeAsync(type.Id, type.Motion, type.CanTravelSpecialTransfer, cancellationToken).ConfigureAwait(false);
            await dispatching.AddAgvTypeAsync(type.Id, type.GoChargeBelowPercent, type.ResumeAtPercent, cancellationToken).ConfigureAwait(false);
        }

        foreach (var agv in data.Agvs)
        {
            await traffic.AddAgvAsync(agv.Id, agv.Type, cancellationToken).ConfigureAwait(false);
            await dispatching.AddAgvAsync(agv.Id, agv.Type, agv.Carrier, cancellationToken).ConfigureAwait(false);
        }

        var configuration = data.Consumables;
        foreach (var processType in configuration.ProcessTypes)
        {
            await consumables.AddProcessTypeAsync(processType, cancellationToken).ConfigureAwait(false);
        }

        foreach (var variety in configuration.Varieties)
        {
            await consumables.AddVarietyAsync(variety.Variety, variety.ProcessType, cancellationToken).ConfigureAwait(false);
        }

        foreach (var device in configuration.Devices)
        {
            await consumables.AddDeviceAsync(device, cancellationToken).ConfigureAwait(false);
        }

        foreach (var station in configuration.Stations)
        {
            await consumables.AddStationAsync(station, cancellationToken).ConfigureAwait(false);
        }

        foreach (var setting in configuration.StockerSettings)
        {
            consumables.SaveStockerSetting(setting.Device, setting.Counts);
        }

        foreach (var arrival in configuration.ExpectedArrivals)
        {
            consumables.AddExpectedArrival(arrival.Consumable, arrival.Variety);
        }

        LogSeeded(logger, path);
        return true;
    }

    /// <summary>読んで、全体を確かめる。正しくなければ、ログに出して投げる。</summary>
    private static SeedData Prepare(string path, ILogger logger)
    {
        var errors = new List<string>();
        SeedData? data = null;
        try
        {
            var file = JsonSerializer.Deserialize<InitialDataFile>(File.ReadAllText(path), JsonOptions)
                ?? throw new JsonException("中身がありません。");
            data = Validate(file, errors);
        }
        catch (IOException exception)
        {
            errors.Add($"ファイルを読めません：{exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            errors.Add($"ファイルを読めません：{exception.Message}");
        }
        catch (JsonException exception)
        {
            errors.Add($"JSON として読めません：{exception.Message}");
        }

        if (errors.Count > 0 || data is null)
        {
            var message = $"初期データ {path} を入れられません。{Environment.NewLine}{string.Join(Environment.NewLine, errors)}";
            LogInvalid(logger, message);
            throw new InitialDataException(message);
        }

        return data;
    }

    /// <summary>中身を確かめて、入れる形にする。正しくないところは errors に足す。</summary>
    private static SeedData? Validate(InitialDataFile file, List<string> errors)
    {
        // ---- マップ ----
        MapSnapshot? map = null;
        if (file.Map is null)
        {
            errors.Add("map がありません。");
        }
        else
        {
            var panels = Each(file.Map.Panels, errors, "パネル", p => new Panel(new PanelId(p.Id), p.Type, p.ChargingPin)
            {
                CarrierLoweringHeading = p.CarrierLoweringHeading is { } heading ? new Heading(heading) : null,
            });
            var connections = Each(file.Map.Connections, errors, "つながり", c => new Connection(new PanelId(c.From), new PanelId(c.To), c.DistanceMm, new Heading(c.Heading), c.OneWay));
            var result = MapSnapshot.Create(panels, connections);
            errors.AddRange(result.Errors.Select(e => $"マップ：{e.Message}"));
            map = result.Snapshot;
        }

        bool IsTransferPanel(PanelId panel) => map?.Panels.Any(p => p.Id == panel && p.Type is PanelType.Transfer or PanelType.SpecialTransfer) ?? false;

        // ---- AGV ----
        var agvTypes = Each(file.AgvTypes, errors, "AGV の種類", t =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(t.Id, "id");
            var motion = new MotionProfile(t.ForwardSpeedMmPerSecond, t.BackwardSpeedMmPerSecond, t.RotationSpeedDegreesPerSecond);
            var goBelow = t.GoChargeBelowPercent ?? DispatchingStore.InitialGoChargeBelowPercent;
            var resumeAt = t.ResumeAtPercent ?? DispatchingStore.InitialResumeAtPercent;
            if (goBelow is < 0 or > 100 || resumeAt is < 0 or > 100 || goBelow >= resumeAt)
            {
                throw new ArgumentException($"充電の設定値（充電に向かう値 {goBelow}%、割り当てに戻す値 {resumeAt}%）が正しくありません。");
            }

            return new AgvTypeSeed(t.Id, motion, t.GoChargeBelowPercent, t.ResumeAtPercent, t.CanTravelSpecialTransfer);
        });
        Unique(agvTypes.Select(t => t.Id), errors, "AGV の種類");

        var agvs = Each(file.Agvs, errors, "AGV", a =>
        {
            if (!agvTypes.Any(t => t.Id == a.Type))
            {
                throw new ArgumentException($"AGV {a.Id} の種類 {a.Type} がありません。");
            }

            return new AgvSeed(new AgvId(a.Id), a.Type, new CarrierTypeId(a.Carrier));
        });
        Unique(agvs.Select(a => a.Id.Value), errors, "AGV");

        // ---- 消耗品の管理 ----
        var consumables = file.Consumables ?? new ConsumablesData();
        var processTypes = Each(consumables.ProcessTypes, errors, "加工の種類", p => new ProcessTypeSettings(new ProcessTypeId(p.Id), new CarrierTypeId(p.Carrier), p.CarrierCapacity));
        Unique(processTypes.Select(p => p.Id.Value), errors, "加工の種類");

        var varieties = Each(consumables.Varieties, errors, "品種", v =>
        {
            var processType = new ProcessTypeId(v.ProcessType);
            return processTypes.Any(p => p.Id == processType)
                ? new VarietyConfiguration(new VarietyId(v.Id), processType)
                : throw new ArgumentException($"品種 {v.Id} の加工の種類 {v.ProcessType} がありません。");
        });
        Unique(varieties.Select(v => v.Variety.Value), errors, "品種");
        bool HasVariety(VarietyId variety) => varieties.Any(v => v.Variety == variety);

        var devices = Each(consumables.Devices, errors, "装置", d =>
        {
            var panel = new PanelId(d.TransferPanel);
            return IsTransferPanel(panel)
                ? new DeviceLayout(new DeviceId(d.Id), panel, d.SlotsPerStocker)
                : throw new ArgumentException($"装置 {d.Id} の受け渡しパネル {d.TransferPanel} が、マップの受け渡しパネルにありません。");
        });
        Unique(devices.Select(d => d.Id.Value), errors, "装置");

        var stations = Each(consumables.Stations, errors, "ステーション", s =>
        {
            var panel = new PanelId(s.TransferPanel);
            return IsTransferPanel(panel)
                ? new StationLayout(new StationId(s.Id), panel)
                : throw new ArgumentException($"ステーション {s.Id} の受け渡しパネル {s.TransferPanel} が、マップの受け渡しパネルにありません。");
        });
        Unique(stations.Select(s => s.Id.Value), errors, "ステーション");

        var stockerSettings = Each(consumables.StockerSettings, errors, "ストッカーの設定", s =>
        {
            var device = new DeviceId(s.Device);
            if (!devices.Any(d => d.Id == device))
            {
                throw new ArgumentException($"ストッカーの設定の装置 {s.Device} がありません。");
            }

            var counts = s.Counts.ToDictionary(pair => new VarietyId(pair.Key), pair => pair.Value);
            foreach (var (variety, count) in counts)
            {
                if (!HasVariety(variety) || count < 0)
                {
                    throw new ArgumentException($"装置 {s.Device} のストッカーの設定の品種 {variety}（{count} 本）が正しくありません。");
                }
            }

            return new StockerSettingConfiguration(device, counts);
        });
        Unique(stockerSettings.Select(s => s.Device.Value), errors, "ストッカーの設定の装置");

        var arrivals = Each(consumables.ExpectedArrivals, errors, "入庫の予定", a =>
        {
            var variety = new VarietyId(a.Variety);
            return HasVariety(variety)
                ? new ExpectedArrival(new ConsumableId(a.Consumable), variety)
                : throw new ArgumentException($"入庫の予定 {a.Consumable} の品種 {a.Variety} がありません。");
        });
        Unique(arrivals.Select(a => a.Consumable.Value), errors, "入庫の予定の消耗品");

        return map is null || errors.Count > 0
            ? null
            : new SeedData(
                map,
                agvTypes,
                agvs,
                new ConsumablesConfiguration(devices, stations, processTypes, varieties, stockerSettings, arrivals));
    }

    /// <summary>要素ごとに作る。作れなかったもの（ID が空、値が範囲外など）は errors に足して飛ばす。</summary>
    private static List<TResult> Each<TSource, TResult>(IEnumerable<TSource> items, List<string> errors, string what, Func<TSource, TResult> create)
    {
        var results = new List<TResult>();
        var index = 0;
        foreach (var item in items)
        {
            index++;
            try
            {
                results.Add(create(item));
            }
            catch (ArgumentException exception)
            {
                errors.Add($"{what}（{index} 番目）：{exception.Message}");
            }
        }

        return results;
    }

    private static void Unique(IEnumerable<string> ids, List<string> errors, string what) =>
        errors.AddRange(ids.GroupBy(id => id, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => $"{what}の ID {g.Key} が重なっています。"));

    private sealed record AgvTypeSeed(string Id, MotionProfile Motion, int? GoChargeBelowPercent, int? ResumeAtPercent, bool CanTravelSpecialTransfer);

    private sealed record AgvSeed(AgvId Id, string Type, CarrierTypeId Carrier);

    private sealed record SeedData(MapSnapshot Map, IReadOnlyList<AgvTypeSeed> AgvTypes, IReadOnlyList<AgvSeed> Agvs, ConsumablesConfiguration Consumables);

    [LoggerMessage(Level = LogLevel.Information, Message = "DB が空なので、初期データ {Path} を入れました。")]
    private static partial void LogSeeded(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Message}")]
    private static partial void LogInvalid(ILogger logger, string message);
}
