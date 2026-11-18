using FlowHub.Consumables;

namespace FlowHub.ToolPlanning.Domain;

/// <summary>
/// 装置のある品種の不足。
/// </summary>
/// <param name="Since">最初に不足を見つけた時刻。</param>
internal sealed record Shortage(DeviceId Device, VarietyId Variety, int Count, DateTimeOffset Since, ProcessTypeSettings ProcessType);

/// <summary>
/// 装置から回収する消耗品。
/// </summary>
/// <param name="Since">回収が必要だと判断した時刻。</param>
internal sealed record Recovery(DeviceId Device, DeviceRecovery Item, DateTimeOffset Since, ProcessTypeSettings ProcessType);

/// <summary>
/// 往復の向き。
/// </summary>
internal enum TripKind
{
    /// <summary>ステーションから装置へ届ける。</summary>
    Delivery,

    /// <summary>装置からステーションへ回収する。</summary>
    Recovery,
}

/// <summary>
/// 1往復ぶんの搬送の要求。
/// </summary>
/// <param name="NeededBy">必要時刻（まとめたものの中で一番早い時刻）。</param>
internal sealed record Trip(TripKind Kind, DeviceId Device, ProcessTypeSettings ProcessType, IReadOnlyList<ConsumableId> Cargo, DateTimeOffset NeededBy);

/// <summary>
/// 届ける往復と、ステーションに足りないぶん（補充を頼む数）。
/// </summary>
internal sealed record DeliveryPlan(IReadOnlyList<Trip> Trips, IReadOnlyDictionary<VarietyId, int> Lacking);

/// <summary>
/// 不足と回収から、1往復ぶんずつの搬送の要求を組み立てる。
/// </summary>
internal static class TripPlanner
{
    /// <summary>
    /// 届ける往復を組み立てる。不足を見つけた時刻の順、同じなら品種の ID の順（複数の装置なら装置の ID の順）に、
    /// ステーションにあるぶんを、装置の空きの数まで割り当て、同じ装置・同じ加工の種類ごとにキャリアに入る個数で分ける。
    /// </summary>
    /// <param name="freeSlots">装置ごとの、届ける要求を出せる数。</param>
    /// <param name="stationStock">ステーションにある、届けられる品種の消耗品（選ぶ順。指名されたものは入らない）。</param>
    /// <param name="designated">その装置へ届けるように指名された、その品種の消耗品（選ぶ順）。先入れ先出しより先に使う。</param>
    public static DeliveryPlan PlanDeliveries(
        IEnumerable<Shortage> shortages,
        IReadOnlyDictionary<DeviceId, int> freeSlots,
        Func<VarietyId, IReadOnlyList<ConsumableId>> stationStock,
        Func<DeviceId, VarietyId, IReadOnlyList<ConsumableId>> designated)
    {
        ArgumentNullException.ThrowIfNull(shortages);
        ArgumentNullException.ThrowIfNull(freeSlots);
        ArgumentNullException.ThrowIfNull(stationStock);
        ArgumentNullException.ThrowIfNull(designated);

        var pools = new Dictionary<VarietyId, Queue<ConsumableId>>();
        var free = new Dictionary<DeviceId, int>(freeSlots);
        var lacking = new Dictionary<VarietyId, int>();
        var picks = new List<(Shortage Shortage, ConsumableId Consumable)>();

        var ordered = shortages
            .OrderBy(s => s.Since)
            .ThenBy(s => s.Variety.Value, StringComparer.Ordinal)
            .ThenBy(s => s.Device.Value, StringComparer.Ordinal);
        foreach (var shortage in ordered)
        {
            if (!pools.TryGetValue(shortage.Variety, out var pool))
            {
                pool = new Queue<ConsumableId>(stationStock(shortage.Variety));
                pools.Add(shortage.Variety, pool);
            }

            // 指名されたものを先に、そのあと先入れ先出しで選ぶ
            var candidates = new Queue<ConsumableId>(designated(shortage.Device, shortage.Variety));
            var available = candidates.Count + pool.Count;
            if (available < shortage.Count)
            {
                lacking[shortage.Variety] = lacking.GetValueOrDefault(shortage.Variety) + shortage.Count - available;
            }

            var count = Math.Min(Math.Min(shortage.Count, available), free.GetValueOrDefault(shortage.Device));
            for (var i = 0; i < count; i++)
            {
                picks.Add((shortage, candidates.Count > 0 ? candidates.Dequeue() : pool.Dequeue()));
            }

            free[shortage.Device] = free.GetValueOrDefault(shortage.Device) - count;
        }

        var trips = picks
            .GroupBy(p => (p.Shortage.Device, p.Shortage.ProcessType))
            .SelectMany(group => group
                .Chunk(group.Key.ProcessType.CarrierCapacity)
                .Select(chunk => new Trip(
                    TripKind.Delivery,
                    group.Key.Device,
                    group.Key.ProcessType,
                    chunk.Select(p => p.Consumable).ToArray(),
                    chunk.Min(p => p.Shortage.Since))))
            .ToArray();

        return new DeliveryPlan(trips, lacking);
    }

    /// <summary>
    /// 回収の往復を組み立てる。不整合、設定にない品種、設定より多い品種、廃棄の順（同じ理由なら品種の ID、消耗品の ID の順。品種がわからないものは先）に積み、
    /// 同じ装置・同じ加工の種類ごとにキャリアに入る個数で分ける。
    /// </summary>
    public static IReadOnlyList<Trip> PlanRecoveries(IEnumerable<Recovery> recoveries)
    {
        ArgumentNullException.ThrowIfNull(recoveries);

        return recoveries
            .OrderBy(r => r.Item.Reason)
            .ThenBy(r => r.Item.Variety?.Value ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(r => r.Item.Consumable.Value, StringComparer.Ordinal)
            .GroupBy(r => (r.Device, r.ProcessType))
            .SelectMany(group => group
                .Chunk(group.Key.ProcessType.CarrierCapacity)
                .Select(chunk => new Trip(
                    TripKind.Recovery,
                    group.Key.Device,
                    group.Key.ProcessType,
                    chunk.Select(r => r.Item.Consumable).ToArray(),
                    chunk.Min(r => r.Since))))
            .ToArray();
    }
}
