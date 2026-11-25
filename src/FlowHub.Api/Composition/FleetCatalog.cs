using System.Text.RegularExpressions;
using FlowHub.Dispatching.Application;
using FlowHub.Dispatching.Infrastructure;
using FlowHub.Notifications;
using FlowHub.Routing;
using FlowHub.Traffic;
using FlowHub.Traffic.Application;
using FlowHub.Traffic.Infrastructure;
using Microsoft.Extensions.Logging;

namespace FlowHub.Api.Composition;

/// <summary>画面に出す AGV の種類（交通整理と配車の両方の値を合わせたもの）。</summary>
/// <param name="Forward">前進の速さ（mm/s）。</param>
/// <param name="Backward">後退の速さ（mm/s）。</param>
/// <param name="Rotation">回転の速さ（°/s）。</param>
/// <param name="ChargeAt">充電に向かう残量（%）。</param>
/// <param name="ResumeAt">割り当てに戻す残量（%）。</param>
/// <param name="Special">特殊消耗品受け渡しパネルを走れるか。</param>
internal sealed record AgvTypeView(string Id, string Name, double Forward, double Backward, double Rotation, int ChargeAt, int ResumeAt, bool Special);

/// <summary>画面に出す、登録してある AGV。</summary>
/// <param name="Type">使う種類の ID。配車にだけ登録してあれば null。</param>
/// <param name="Carrier">キャリアの種類。交通整理にだけ登録してあれば null。</param>
/// <param name="Registration"><c>both</c>、<c>trafficOnly</c>、<c>dispatchOnly</c>。</param>
internal sealed record AgvRegistrationView(string Id, string? Type, string? Carrier, string Registration);

/// <summary>AGV と AGV の種類の登録の、今の様子。</summary>
/// <param name="Types">種類（ID の順。交通整理と配車の両方にあるもの）。</param>
/// <param name="Agvs">登録してある AGV（ID の順。片方にしか登録していないものも）。</param>
/// <param name="DefaultChargeAt">充電に向かう残量の既定値（種類を加えるときに省いたら、これで埋める）。</param>
/// <param name="DefaultResumeAt">割り当てに戻す残量の既定値。</param>
internal sealed record FleetState(IReadOnlyList<AgvTypeView> Types, IReadOnlyList<AgvRegistrationView> Agvs, int DefaultChargeAt, int DefaultResumeAt)
{
    public bool Equals(FleetState? other) =>
        other is not null
        && Types.SequenceEqual(other.Types)
        && Agvs.SequenceEqual(other.Agvs)
        && DefaultChargeAt == other.DefaultChargeAt
        && DefaultResumeAt == other.DefaultResumeAt;

    public override int GetHashCode() => HashCode.Combine(Types.Count, Agvs.Count, DefaultChargeAt, DefaultResumeAt);
}

/// <summary>画面から加える・変える AGV の種類。</summary>
/// <param name="ChargeAt">充電に向かう残量（%）。加えるときは省ける（既定値で埋める）。</param>
/// <param name="ResumeAt">割り当てに戻す残量（%）。加えるときは省ける。</param>
internal sealed record AgvTypeInput(string Id, string Name, double Forward, double Backward, double Rotation, int? ChargeAt, int? ResumeAt, bool Special);

/// <summary>AGV の種類の操作の結果。断ったときと失敗したときは、何も変えていない（元に戻した）。</summary>
internal enum AgvTypeOperationResult
{
    Saved,
    Deleted,
    IdTaken,
    NameTaken,
    NotFound,
    InUse,

    /// <summary>片方の DB に書けなかったので、先に書いた方を元に戻した。</summary>
    Failed,
}

/// <summary>
/// AGV と AGV の種類の登録（交通整理と配車の両方の DB にまたがる）。種類を加える・変える・消すと、両方の DB と、動いている AGV に効かせる。
/// </summary>
/// <remarks>
/// 種類は、交通整理（速さ、走れるか、名前）に先に書き、配車（充電の残量）に書けなければ、交通整理を元に戻す（docs/domain.md）。
/// 元に戻すのにも失敗したら、ログに残して画面のメッセージで知らせる（起動したときの登録の食い違いと同じ扱いになる）。
/// 画面へ送る様子は、メモリに写しておき、変えたときに読み直す（画面へ送るたびに DB を読まないため）。操作は1つずつ行う。
/// </remarks>
internal sealed partial class FleetCatalog(
    TrafficStore trafficStore,
    DispatchingStore dispatchingStore,
    TrafficService traffic,
    DispatchingService dispatching,
    INoticeBoard notices,
    ILogger logger) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile FleetState _current = new([], [], 0, 0);

    /// <summary>今の様子（最後に読み直したもの）。</summary>
    public FleetState Current => _current;

    public void Dispose() => _gate.Dispose();

    /// <summary>DB から読み直す（起動したときと、変えたあと）。</summary>
    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        var trafficTypes = await trafficStore.LoadAgvTypesAsync(cancellationToken).ConfigureAwait(false);
        var dispatchingTypes = (await dispatchingStore.LoadAgvTypesAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(t => t.Id);
        var types = trafficTypes
            .Where(t => dispatchingTypes.ContainsKey(t.Id))
            .Select(t =>
            {
                var d = dispatchingTypes[t.Id];
                return new AgvTypeView(t.Id, t.Name, t.Motion.ForwardSpeedMmPerSecond, t.Motion.BackwardSpeedMmPerSecond, t.Motion.RotationSpeedDegreesPerSecond, d.GoChargeBelowPercent, d.ResumeAtPercent, t.CanTravelSpecialTransfer);
            })
            .ToArray();

        var inTraffic = (await trafficStore.LoadAgvRegistrationsAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(a => a.Id);
        var inDispatching = (await dispatchingStore.LoadAgvRegistrationsAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(a => a.Id);
        var agvs = inTraffic.Keys.Union(inDispatching.Keys)
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .Select(id => (Traffic: inTraffic.GetValueOrDefault(id), Dispatching: inDispatching.GetValueOrDefault(id), Id: id))
            .Select(a => new AgvRegistrationView(
                a.Id.Value,
                a.Traffic?.TypeId ?? a.Dispatching?.TypeId,
                a.Dispatching?.Carrier.Value,
                a.Traffic is null ? "dispatchOnly" : a.Dispatching is null ? "trafficOnly" : "both"))
            .ToArray();

        var (chargeAt, resumeAt) = await dispatchingStore.ChargingDefaultsAsync(cancellationToken).ConfigureAwait(false);
        _current = new FleetState(types, agvs, chargeAt, resumeAt);
    }

    /// <summary>AGV の種類を加える。交通整理に書いてから配車に書き、配車に書けなければ交通整理を元に戻す。</summary>
    /// <exception cref="ArgumentException">ID、名前、値が決まりに合わないとき（頼みの誤り）。</exception>
    public Task<AgvTypeOperationResult> AddTypeAsync(AgvTypeInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var settings = Validate(input, requireCharge: false);
        return RunAsync(async () =>
        {
            var saved = await trafficStore.AddAgvTypeAsync(settings, cancellationToken).ConfigureAwait(false);
            if (saved != AgvTypeSaveResult.Saved)
            {
                return saved == AgvTypeSaveResult.IdTaken ? AgvTypeOperationResult.IdTaken : AgvTypeOperationResult.NameTaken;
            }

            try
            {
                await dispatchingStore.AddAgvTypeAsync(input.Id, input.ChargeAt, input.ResumeAt, cancellationToken).ConfigureAwait(false);
                return AgvTypeOperationResult.Saved;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await UndoAsync(exception, input.Id, () => trafficStore.DeleteAgvTypeAsync(input.Id, CancellationToken.None)).ConfigureAwait(false);
                return AgvTypeOperationResult.Failed;
            }
        }, cancellationToken);
    }

    /// <summary>AGV の種類の名前と値を変え、その種類を使う動いている AGV に効かせる。配車に書けなければ、交通整理を元に戻す。</summary>
    public Task<AgvTypeOperationResult> UpdateTypeAsync(AgvTypeInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var settings = Validate(input, requireCharge: true);
        return RunAsync(async () =>
        {
            var before = (await trafficStore.LoadAgvTypesAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(t => t.Id == input.Id);
            if (before is null)
            {
                return AgvTypeOperationResult.NotFound;
            }

            var saved = await trafficStore.UpdateAgvTypeAsync(settings, cancellationToken).ConfigureAwait(false);
            if (saved != AgvTypeSaveResult.Saved)
            {
                return saved == AgvTypeSaveResult.NameTaken ? AgvTypeOperationResult.NameTaken : AgvTypeOperationResult.NotFound;
            }

            try
            {
                if (await dispatchingStore.UpdateAgvTypeAsync(input.Id, input.ChargeAt!.Value, input.ResumeAt!.Value, cancellationToken).ConfigureAwait(false) != AgvTypeChangeResult.Changed)
                {
                    throw new InvalidOperationException($"AGV の種類 {input.Id} が、配車の DB にありません。");
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await UndoAsync(exception, input.Id, () => trafficStore.UpdateAgvTypeAsync(before, CancellationToken.None)).ConfigureAwait(false);
                return AgvTypeOperationResult.Failed;
            }

            // 動いている AGV（起動したときに両方に登録してあったもの）に効かせる
            var running = traffic.ListAgvs().Select(a => a.Id).ToHashSet();
            foreach (var agv in (await trafficStore.AgvsOfTypeAsync(input.Id, cancellationToken).ConfigureAwait(false)).Where(running.Contains))
            {
                traffic.UpdateAgvMotion(agv, settings.Motion, settings.CanTravelSpecialTransfer);
                dispatching.UpdateChargingPolicy(agv, input.ChargeAt.Value, input.ResumeAt!.Value);
            }

            return AgvTypeOperationResult.Saved;
        }, cancellationToken);
    }

    /// <summary>AGV の種類を消す（使う AGV がないときだけ）。配車から消せなければ、交通整理を元に戻す。</summary>
    public Task<AgvTypeOperationResult> DeleteTypeAsync(string id, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var before = (await trafficStore.LoadAgvTypesAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(t => t.Id == id);
            if (before is null)
            {
                return AgvTypeOperationResult.NotFound;
            }

            // 配車の側に、その種類を使う AGV がいないかも先に確かめる（交通整理を消してから断らないため）
            if ((await dispatchingStore.AgvsOfTypeAsync(id, cancellationToken).ConfigureAwait(false)).Count > 0)
            {
                return AgvTypeOperationResult.InUse;
            }

            switch (await trafficStore.DeleteAgvTypeAsync(id, cancellationToken).ConfigureAwait(false))
            {
                case AgvTypeDeleteResult.InUse:
                    return AgvTypeOperationResult.InUse;
                case AgvTypeDeleteResult.NotFound:
                    return AgvTypeOperationResult.NotFound;
                case AgvTypeDeleteResult.Deleted:
                    break;
                default:
                    throw new InvalidOperationException("知らない結果です。");
            }

            try
            {
                await dispatchingStore.DeleteAgvTypeAsync(id, cancellationToken).ConfigureAwait(false);
                return AgvTypeOperationResult.Deleted;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await UndoAsync(exception, id, () => trafficStore.AddAgvTypeAsync(before, CancellationToken.None)).ConfigureAwait(false);
                return AgvTypeOperationResult.Failed;
            }
        }, cancellationToken);

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex TypeIdPattern();

    /// <summary>決まり：ID は英数字と「-」「_」、名前は空でない、速さは 1 以上の整数、残量は 0〜100 で、戻す値は向かう値以上。</summary>
    private static AgvTypeSettings Validate(AgvTypeInput input, bool requireCharge)
    {
        if (!TypeIdPattern().IsMatch(input.Id))
        {
            throw new ArgumentException($"種類の ID は、英数字と「-」「_」で入れてください（{input.Id}）。", nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            throw new ArgumentException("種類の名前を入れてください。", nameof(input));
        }

        foreach (var speed in new[] { input.Forward, input.Backward, input.Rotation })
        {
            if (speed < 1 || speed != Math.Floor(speed))
            {
                throw new ArgumentException($"速さは 1 以上の整数で入れてください（{speed}）。", nameof(input));
            }
        }

        if (requireCharge && (input.ChargeAt is null || input.ResumeAt is null))
        {
            throw new ArgumentException("充電に向かう残量と、割り当てに戻す残量を入れてください。", nameof(input));
        }

        if (input.ChargeAt is < 0 or > 100 || input.ResumeAt is < 0 or > 100 || (input.ChargeAt is { } go && input.ResumeAt is { } resume && resume < go))
        {
            throw new ArgumentException("残量は 0〜100 で、割り当てに戻す残量は充電に向かう残量以上にしてください。", nameof(input));
        }

        return new AgvTypeSettings(input.Id, input.Name.Trim(), new MotionProfile(input.Forward, input.Backward, input.Rotation), input.Special);
    }

    private async Task<AgvTypeOperationResult> RunAsync(Func<Task<AgvTypeOperationResult>> operation, CancellationToken cancellationToken)
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

    /// <summary>配車に書けなかったので、交通整理を元に戻す。戻すのにも失敗したら、ログに残して画面のメッセージで知らせる。</summary>
    private async Task UndoAsync(Exception cause, string typeId, Func<Task> undo)
    {
        LogDispatchingFailed(logger, cause, typeId);
        try
        {
            await undo().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogUndoFailed(logger, exception, typeId);
            await notices.PostAsync(
                "AGV の種類の登録が食い違っています",
                $"種類 {typeId} を保存するときに、走行と配車の片方にだけ書けたまま、元に戻せませんでした。AGV と AGV の種類の登録の画面で確かめてください。",
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "AGV の種類 {TypeId} を配車の DB に書けなかったので、交通整理の DB を元に戻します。")]
    private static partial void LogDispatchingFailed(ILogger logger, Exception exception, string typeId);

    [LoggerMessage(Level = LogLevel.Error, Message = "AGV の種類 {TypeId} の交通整理の DB を、元に戻せませんでした。走行と配車の登録が食い違っています。")]
    private static partial void LogUndoFailed(ILogger logger, Exception exception, string typeId);
}
