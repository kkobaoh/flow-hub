using FlowHub.Map;

namespace FlowHub.Dispatching.Domain;

/// <summary>
/// 割り当てた搬送と、そのときに頼むこと。
/// </summary>
internal sealed record Assignment(Transport Transport, IReadOnlyList<TransportAction> Actions);

/// <summary>
/// 割り当て待ちの搬送の待ち行列。必要時刻が早い順、同じなら頼まれた順に並べる。
/// </summary>
internal sealed class TransportQueue
{
    private readonly List<Transport> _waiting = [];
    private long _nextId = 1;

    /// <summary>割り当て待ちの搬送（並べた順）。</summary>
    public IReadOnlyList<Transport> Waiting =>
        _waiting
            .OrderBy(t => t.Request.NeededBy)
            .ThenBy(t => t.Id.Value)
            .ToArray();

    /// <summary>
    /// 搬送を頼まれた。番号を付けて待ち行列に入れる。
    /// </summary>
    public Transport Add(TransportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var transport = new Transport(new TransportId(_nextId++), request);
        _waiting.Add(transport);
        return transport;
    }

    /// <summary>
    /// 割り当て待ちの搬送に、並べた順に AGV を割り当てる。1台の AGV には1つの搬送だけを割り当てる。
    /// AGV が空いたとき、充電残量が変わったとき、新しい要求が来たときに呼ぶ。
    /// </summary>
    /// <param name="costOf">AGV の今の位置から、搬送元の受け渡しパネルまでの移動コスト。経路が見つからなければ null。</param>
    /// <param name="isSpecialTransfer">特殊消耗品受け渡しパネルか（搬送元か搬送先がそうなら、走れる AGV にだけ割り当てる）。</param>
    public IReadOnlyList<Assignment> AssignWaiting(
        IEnumerable<AgvCandidate> candidates,
        Func<AgvCandidate, PanelId, TimeSpan?> costOf,
        Func<PanelId, bool>? isSpecialTransfer = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(costOf);

        var available = candidates.ToList();
        var assignments = new List<Assignment>();

        foreach (var transport in Waiting)
        {
            // AGV 自身が搬送元なら、その AGV にだけ割り当てる
            if (transport.Request.FromAgv is { } fromAgv)
            {
                if (available.SingleOrDefault(candidate => candidate.Id == fromAgv) is { CanTakeTransport: true })
                {
                    assignments.Add(new Assignment(transport, transport.Assign(fromAgv)));
                    _waiting.Remove(transport);
                    available.RemoveAll(candidate => candidate.Id == fromAgv);
                }

                continue;
            }

            var source = transport.Request.Source.Panel;
            var special = isSpecialTransfer is not null && (isSpecialTransfer(source) || isSpecialTransfer(transport.Request.Destination.Panel));
            var request = new AssignmentRequest(source, transport.Request.Carrier, special);
            if (AgvAssigner.Choose(request, available, candidate => costOf(candidate, source)) is not { } agv)
            {
                continue;
            }

            assignments.Add(new Assignment(transport, transport.Assign(agv)));
            _waiting.Remove(transport);
            available.RemoveAll(candidate => candidate.Id == agv);
        }

        return assignments;
    }

    /// <summary>
    /// 割り当て前の搬送を取りやめる。
    /// </summary>
    public void Cancel(TransportId id)
    {
        var transport = _waiting.SingleOrDefault(t => t.Id == id)
            ?? throw new InvalidOperationException($"搬送 {id} は割り当て待ちの中にありません。");

        transport.Cancel();
        _waiting.Remove(transport);
    }
}
