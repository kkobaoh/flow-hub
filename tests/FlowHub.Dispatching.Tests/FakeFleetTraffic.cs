using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic;

namespace FlowHub.Dispatching.Tests;

/// <summary>複数台の AGV を置ける交通整理の偽物。頼まれた移動を記録する。移動コストは決めておける（決めていなければ 10秒）。</summary>
internal sealed class FakeFleetTraffic : ITrafficService
{
    public Dictionary<AgvId, AgvView> Agvs { get; } = [];

    public HashSet<PanelId> ReservedByOthers { get; } = [];

    public Dictionary<(AgvId Agv, PanelId To), TimeSpan> Costs { get; } = [];

    public List<(AgvId Agv, PanelId To, string CorrelationId)> Moves { get; } = [];

    public AgvView GetAgv(AgvId id) => Agvs[id];

    public TimeSpan? EstimateMoveCost(AgvId id, PanelId destination) =>
        Costs.TryGetValue((id, destination), out var cost) ? cost : TimeSpan.FromSeconds(10);

    public bool IsReservedByOther(PanelId panel, AgvId self) => ReservedByOthers.Contains(panel);

    public Task<MoveResult> MoveAgvAsync(AgvId id, PanelId destination, DateTimeOffset? neededBy, string correlationId, CancellationToken cancellationToken)
    {
        Moves.Add((id, destination, correlationId));
        return Task.FromResult(MoveResult.Started);
    }

    public Task LowerCarrierAsync(AgvId id, string correlationId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RaiseCarrierAsync(AgvId id, string correlationId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> ChangeControlStatusAsync(AgvId id, ControlStatus target, Pose? position, CancellationToken cancellationToken)
    {
        Agvs[id] = Agvs[id] with { Control = target };
        return Task.FromResult(true);
    }

    public void SetEvacuationEnabled(bool enabled)
    {
    }

    public Task StopAgvAsync(AgvId id, string correlationId, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>AGV を置く。走っているなら、止まれる最初の位置も渡す。</summary>
    public void Set(AgvId id, PanelId at, PanelId? stopsAt = null, ControlStatus control = ControlStatus.Auto, bool canTravelSpecialTransfer = false) =>
        Agvs[id] = new AgvView(id, new Pose(at.Value, 0), stopsAt is null ? AgvState.Idle : AgvState.Moving, CarrierRaised: true, CumulativeDistanceMm: 0, control)
        {
            StopsAt = new Pose((stopsAt ?? at).Value, 0),
            CanTravelSpecialTransfer = canTravelSpecialTransfer,
        };
}
