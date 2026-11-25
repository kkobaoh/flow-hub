using FlowHub.Dispatching;
using FlowHub.Dispatching.Infrastructure;
using FlowHub.Traffic;
using FlowHub.Traffic.Infrastructure;

namespace FlowHub.Api.Composition;

/// <summary>AGV の登録の操作の結果。断ったときと失敗したときは、何も変えていない（元に戻した）。</summary>
internal enum AgvEntryOperationResult
{
    Saved,
    Deleted,

    /// <summary>その ID の AGV は、もう登録してある。</summary>
    IdTaken,

    NotFound,

    /// <summary>切り離しでない（変える・消すのは、切り離しのときだけ）。</summary>
    NotDetached,

    /// <summary>取りやめで、ものを積んだまま止まっている（消すのを断る）。</summary>
    Loaded,

    /// <summary>片方の DB に書けなかったので、先に書いた方を元に戻した。</summary>
    Failed,
}

/// <summary>
/// AGV の登録（加える・変える・そろえる・消す）。交通整理と配車の両方の DB と、動かす AGV に効かせる。
/// </summary>
/// <remarks>
/// 交通整理に先に書き、配車に書けなければ、交通整理を元に戻す（種類と同じ扱い。docs/domain.md）。
/// 加えた AGV、種類を入れた AGV、そろえた AGV は、切り離しから始める。
/// </remarks>
internal sealed partial class FleetCatalog
{
    /// <summary>AGV を加える（両方に登録し、切り離しの AGV として動かす AGV に入れる）。</summary>
    /// <exception cref="ArgumentException">ID が空、知らない種類、選べないキャリアの種類のとき（頼みの誤り）。</exception>
    public Task<AgvEntryOperationResult> AddAgvAsync(string id, string typeId, string carrier, CancellationToken cancellationToken) =>
        RunAgvAsync(async () =>
        {
            var agv = ToAgvId(id);
            var (motion, charge) = await TypeOfAsync(typeId, cancellationToken).ConfigureAwait(false);
            var carrierType = ToCarrier(carrier);
            if (_current.Agvs.Any(a => a.Id == agv.Value))
            {
                return AgvEntryOperationResult.IdTaken;
            }

            await trafficStore.AddAgvAsync(agv, typeId, cancellationToken).ConfigureAwait(false);
            if (!await TryWriteDispatchingAsync(agv, () => dispatchingStore.AddAgvAsync(agv, typeId, carrierType, CancellationToken.None), () => trafficStore.RemoveAgvAsync(agv, CancellationToken.None)).ConfigureAwait(false))
            {
                return AgvEntryOperationResult.Failed;
            }

            Run(agv, motion, charge, carrierType);
            return AgvEntryOperationResult.Saved;
        }, cancellationToken);

    /// <summary>
    /// AGV の種類とキャリアの種類を変える（切り離しのときだけ）。種類が決まっていなかった AGV（自動で登録したもの）なら、両方に登録して動かす AGV に入れる。
    /// </summary>
    public Task<AgvEntryOperationResult> ChangeAgvAsync(string id, string typeId, string carrier, CancellationToken cancellationToken) =>
        RunAgvAsync(async () =>
        {
            var agv = ToAgvId(id);
            var (motion, charge) = await TypeOfAsync(typeId, cancellationToken).ConfigureAwait(false);
            var carrierType = ToCarrier(carrier);
            var entry = _current.Agvs.FirstOrDefault(a => a.Id == agv.Value);
            switch (entry?.Registration)
            {
                case null:
                case "trafficOnly":
                case "dispatchOnly":
                    return AgvEntryOperationResult.NotFound; // 食い違っている AGV は、そろえる
                case "unconfigured":
                    await trafficStore.SetAgvTypeAsync(agv, typeId, cancellationToken).ConfigureAwait(false);
                    if (!await TryWriteDispatchingAsync(agv, () => dispatchingStore.AddAgvAsync(agv, typeId, carrierType, CancellationToken.None), () => RevertToUnconfiguredAsync(agv)).ConfigureAwait(false))
                    {
                        return AgvEntryOperationResult.Failed;
                    }

                    Run(agv, motion, charge, carrierType);
                    return AgvEntryOperationResult.Saved;
                case "both":
                    if (traffic.ListAgvs().FirstOrDefault(a => a.Id == agv) is { } running && running.Control != ControlStatus.Detached)
                    {
                        return AgvEntryOperationResult.NotDetached;
                    }

                    var before = entry.Type!;
                    await trafficStore.SetAgvTypeAsync(agv, typeId, cancellationToken).ConfigureAwait(false);
                    if (!await TryWriteDispatchingAsync(agv, () => dispatchingStore.UpdateAgvAsync(agv, typeId, carrierType, CancellationToken.None), () => trafficStore.SetAgvTypeAsync(agv, before, CancellationToken.None)).ConfigureAwait(false))
                    {
                        return AgvEntryOperationResult.Failed;
                    }

                    if (traffic.ListAgvs().Any(a => a.Id == agv))
                    {
                        traffic.UpdateAgvMotion(agv, motion.Motion, motion.CanTravelSpecialTransfer);
                        dispatching.UpdateChargingPolicy(agv, charge.GoChargeBelowPercent, charge.ResumeAtPercent);
                        dispatching.UpdateCarrier(agv, carrierType);
                    }

                    return AgvEntryOperationResult.Saved;
                default:
                    throw new InvalidOperationException($"知らない登録の様子です（{entry.Registration}）。");
            }
        }, cancellationToken);

    /// <summary>
    /// 登録が食い違っている AGV を、足りない側に登録してそろえる（配車にないならキャリアの種類を、走行にないなら種類を入れる）。そろえた AGV は、切り離しから始める。
    /// </summary>
    public Task<AgvEntryOperationResult> AlignAgvAsync(string id, string? typeId, string? carrier, CancellationToken cancellationToken) =>
        RunAgvAsync(async () =>
        {
            var agv = ToAgvId(id);
            var entry = _current.Agvs.FirstOrDefault(a => a.Id == agv.Value);
            switch (entry?.Registration)
            {
                case "trafficOnly":
                {
                    var carrierType = ToCarrier(carrier);
                    var (motion, charge) = await TypeOfAsync(entry.Type!, cancellationToken).ConfigureAwait(false);
                    if (!await TryWriteDispatchingAsync(agv, () => dispatchingStore.AddAgvAsync(agv, entry.Type!, carrierType, CancellationToken.None), () => Task.CompletedTask).ConfigureAwait(false))
                    {
                        return AgvEntryOperationResult.Failed;
                    }

                    trafficStore.Save(agv, ControlStatus.Detached);
                    Run(agv, motion, charge, carrierType);
                    return AgvEntryOperationResult.Saved;
                }

                case "dispatchOnly":
                {
                    var type = typeId ?? throw new ArgumentException("AGV の種類を選んでください。", nameof(typeId));
                    var (motion, charge) = await TypeOfAsync(type, cancellationToken).ConfigureAwait(false);
                    var carrierType = new CarrierTypeId(entry.Carrier!);
                    await trafficStore.AddAgvAsync(agv, type, cancellationToken).ConfigureAwait(false);
                    if (!await TryWriteDispatchingAsync(agv, () => dispatchingStore.UpdateAgvAsync(agv, type, carrierType, CancellationToken.None), () => trafficStore.RemoveAgvAsync(agv, CancellationToken.None)).ConfigureAwait(false))
                    {
                        return AgvEntryOperationResult.Failed;
                    }

                    Run(agv, motion, charge, carrierType);
                    return AgvEntryOperationResult.Saved;
                }

                default:
                    return AgvEntryOperationResult.NotFound;
            }
        }, cancellationToken);

    /// <summary>
    /// AGV の登録を消す（切り離しのときだけ。取りやめでものを積んだまま止まっていれば断る）。食い違っている AGV と、種類が決まっていない AGV も消せる。
    /// </summary>
    public Task<AgvEntryOperationResult> DeleteAgvAsync(string id, CancellationToken cancellationToken) =>
        RunAgvAsync(async () =>
        {
            var agv = ToAgvId(id);
            if (_current.Agvs.All(a => a.Id != agv.Value))
            {
                return AgvEntryOperationResult.NotFound;
            }

            if (dispatching.IsLoaded(agv))
            {
                return AgvEntryOperationResult.Loaded;
            }

            if (traffic.UnregisterAgv(agv) == AgvUnregisterResult.NotDetached)
            {
                return AgvEntryOperationResult.NotDetached;
            }

            // 切り離しの AGV は搬送を持たない。配車から外せなかったら、交通整理に戻すことはせず、知らせる
            var removal = dispatching.UnregisterAgv(agv);
            if (removal is AgvRemovalResult.HasTransport or AgvRemovalResult.Loaded)
            {
                throw new InvalidOperationException($"{agv} を配車から外せませんでした（{removal}）。");
            }

            await trafficStore.RemoveAgvAsync(agv, cancellationToken).ConfigureAwait(false);
            await dispatchingStore.RemoveAgvAsync(agv, cancellationToken).ConfigureAwait(false);
            return AgvEntryOperationResult.Deleted;
        }, cancellationToken);

    private static AgvId ToAgvId(string id) =>
        string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("AGV の番号を入れてください。", nameof(id)) : new AgvId(id.Trim());

    private CarrierTypeId ToCarrier(string? carrier) =>
        carrier is not null && carriers().Any(c => c.Value == carrier)
            ? new CarrierTypeId(carrier)
            : throw new ArgumentException($"キャリアの種類は、加工の種類にあるものから選んでください（{carrier}）。", nameof(carrier));

    /// <summary>種類の値（交通整理と配車の両方）。知らない種類なら、頼みの誤り。</summary>
    private async Task<(AgvTypeSettings Motion, DispatchingAgvTypeSettings Charge)> TypeOfAsync(string typeId, CancellationToken cancellationToken)
    {
        var motion = (await trafficStore.LoadAgvTypesAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(t => t.Id == typeId);
        var charge = (await dispatchingStore.LoadAgvTypesAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(t => t.Id == typeId);
        return motion is not null && charge is not null ? (motion, charge) : throw new ArgumentException($"AGV の種類 {typeId} は登録されていません。", nameof(typeId));
    }

    /// <summary>両方に登録した AGV を、動かす AGV に入れる（コントロールステータスは DB から読む。新しく入れたものは切り離し）。</summary>
    private void Run(AgvId agv, AgvTypeSettings motion, DispatchingAgvTypeSettings charge, CarrierTypeId carrier)
    {
        traffic.RegisterAgv(agv, motion.Motion, motion.CanTravelSpecialTransfer);
        dispatching.RegisterAgv(agv, carrier, charge.GoChargeBelowPercent, charge.ResumeAtPercent);
    }

    /// <summary>種類を入れた交通整理を、種類が決まっていない AGV に戻す。</summary>
    private async Task RevertToUnconfiguredAsync(AgvId agv)
    {
        await trafficStore.RemoveAgvAsync(agv, CancellationToken.None).ConfigureAwait(false);
        await trafficStore.AddUnconfiguredAgvAsync(agv, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>配車の DB に書く。書けなければ、交通整理を元に戻して false。</summary>
    private async Task<bool> TryWriteDispatchingAsync(AgvId agv, Func<Task> write, Func<Task> undo)
    {
        try
        {
            await write().ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await UndoAsync(exception, agv.Value, undo).ConfigureAwait(false);
            return false;
        }
    }

    private async Task<AgvEntryOperationResult> RunAgvAsync(Func<Task<AgvEntryOperationResult>> operation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await operation().ConfigureAwait(false);
            await ReloadAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }
}
