using FlowHub.Map;
using FlowHub.Traffic;

namespace FlowHub.Dispatching.Domain;

/// <summary>
/// AGV の充電の状態。
/// </summary>
internal enum ChargingState
{
    /// <summary>通常。搬送の割り当てに使える。</summary>
    Normal,

    /// <summary>充電に向かう値を下回ったが、搬送の途中なので、搬送を終えてから充電ポートを探す。</summary>
    PendingAfterTransport,

    /// <summary>充電ポートを探している。</summary>
    SeekingPort,

    /// <summary>行ける充電ポートがないので、空くのを待っている（画面で知らせる）。</summary>
    WaitingForPort,

    /// <summary>充電ポートへ向かっている。</summary>
    GoingToPort,

    /// <summary>充電ピンのあるパネルで充電している。</summary>
    Charging,
}

/// <summary>
/// 充電の状態が変わったときに、呼び出す側がすること。
/// </summary>
internal enum ChargingDecision
{
    /// <summary>何もしない。</summary>
    None,

    /// <summary>充電ポートを探して、向かわせる（なければ待たせる）。</summary>
    FindPort,

    /// <summary>その場で充電する（充電ピンのあるパネルにいる）。</summary>
    ChargeInPlace,

    /// <summary>割り当てに戻す値まで回復した。割り当てに戻す。</summary>
    Recovered,
}

/// <summary>
/// 充電残量が変わったときの AGV の様子。
/// </summary>
/// <param name="OnChargingPinPanel">充電ピンのあるパネルにいるか。</param>
/// <param name="HasActiveTransport">搬送の途中か。</param>
internal sealed record AgvSituation(bool OnChargingPinPanel, bool HasActiveTransport);

/// <summary>
/// 1台の AGV の充電の判断。充電に向かう値と割り当てに戻す値で、充電に向かわせ、割り当てに戻す。
/// </summary>
internal sealed class AgvCharging(AgvId agv, ChargingPolicy policy)
{
    /// <summary>充電に向かう値と割り当てに戻す値（AGV の種類の値を変えたら、置き換える）。</summary>
    private ChargingPolicy _policy = policy;

    public AgvId Agv { get; } = agv;

    /// <summary>充電の値を置き換える。次の充電残量の知らせから、新しい値で判断する。</summary>
    public void ChangePolicy(ChargingPolicy policy) => _policy = policy ?? throw new ArgumentNullException(nameof(policy));

    public ChargingState State { get; private set; } = ChargingState.Normal;

    /// <summary>最新の充電残量。まだ通知がなければ null。</summary>
    public int? BatteryPercent { get; private set; }

    /// <summary>向かっている充電ポート。</summary>
    public PanelId? Port { get; private set; }

    /// <summary>充電に向かったあと、まだ割り当てに戻していないか（割り当ての判断に使う）。</summary>
    public bool RecoveringCharge => State != ChargingState.Normal;

    /// <summary>
    /// 充電残量が変わった（AGV から通知された）。
    /// </summary>
    public ChargingDecision OnBatteryChanged(int percent, AgvSituation situation)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(percent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100);
        ArgumentNullException.ThrowIfNull(situation);

        BatteryPercent = percent;

        switch (State)
        {
            case ChargingState.Normal when percent < _policy.GoChargeBelowPercent:
                // 搬送の途中なら、充電ピンのあるパネルにいても、搬送を終えてから充電する（届けることを優先する）
                if (situation.HasActiveTransport)
                {
                    State = ChargingState.PendingAfterTransport;
                    return ChargingDecision.None;
                }

                return StartCharging(situation);

            case ChargingState.Charging when percent >= _policy.ResumeAtPercent:
                return Recover();

            default:
                // 向かっている途中に回復しても、向かうのはやめず、着いてから判断する
                return ChargingDecision.None;
        }
    }

    /// <summary>
    /// 搬送を終えた。充電を待っていたなら、搬送を終えたパネルに充電ピンがあればその場で充電し、なければ充電ポートを探す。
    /// </summary>
    public ChargingDecision OnTransportFinished(AgvSituation situation)
    {
        ArgumentNullException.ThrowIfNull(situation);

        return State == ChargingState.PendingAfterTransport
            ? StartCharging(situation)
            : ChargingDecision.None;
    }

    /// <summary>
    /// 充電を始める。充電ピンのあるパネルにいればその場で充電し、なければ充電ポートを探す。
    /// </summary>
    private ChargingDecision StartCharging(AgvSituation situation)
    {
        if (situation.OnChargingPinPanel)
        {
            State = ChargingState.Charging;
            return ChargingDecision.ChargeInPlace;
        }

        State = ChargingState.SeekingPort;
        return ChargingDecision.FindPort;
    }

    /// <summary>
    /// 充電ポートへ向かわせる。
    /// </summary>
    public void GoTo(PanelId port)
    {
        if (State is not (ChargingState.SeekingPort or ChargingState.WaitingForPort))
        {
            throw new InvalidOperationException($"{Agv} は充電ポートを探していないため、向かわせられません（今の状態：{State}）。");
        }

        Port = port;
        State = ChargingState.GoingToPort;
    }

    /// <summary>
    /// 行ける充電ポートがないので、空くのを待たせる。
    /// </summary>
    public void WaitForPort()
    {
        if (State is not (ChargingState.SeekingPort or ChargingState.WaitingForPort))
        {
            throw new InvalidOperationException($"{Agv} は充電ポートを探していないため、待たせられません（今の状態：{State}）。");
        }

        State = ChargingState.WaitingForPort;
    }

    /// <summary>
    /// AGV が「異常」になった。充電ポートへ向かっていたなら、位置リセットで経路が捨てられて着かないので、
    /// 「充電ポートを探す」に戻す（位置リセットが完了して待機中になったら、もう一度探す）。
    /// </summary>
    public void OnFaulted() => OnRouteDiscarded();

    /// <summary>
    /// 交通整理が行き先を捨てた（異常になった、マニュアルやロックに切り替えた）。充電ポートへ向かっていたなら、
    /// 着かないので「充電ポートを探す」に戻す（また動かせるようになったら、もう一度探す）。
    /// </summary>
    public void OnRouteDiscarded()
    {
        if (State != ChargingState.GoingToPort)
        {
            return;
        }

        State = ChargingState.SeekingPort;
        Port = null;
    }

    /// <summary>
    /// AGV がパネルに着いた。向かっていた充電ポートなら充電を始め、すでに回復していれば割り当てに戻す。
    /// </summary>
    public ChargingDecision OnArrived(PanelId panel)
    {
        if (State != ChargingState.GoingToPort || panel != Port)
        {
            return ChargingDecision.None;
        }

        State = ChargingState.Charging;
        return BatteryPercent >= _policy.ResumeAtPercent ? Recover() : ChargingDecision.None;
    }

    private ChargingDecision Recover()
    {
        State = ChargingState.Normal;
        Port = null;
        return ChargingDecision.Recovered;
    }
}

/// <summary>
/// 向かう充電ポートを選ぶ。
/// </summary>
internal static class ChargingPortSelector
{
    /// <summary>
    /// ほかの AGV が使っていない充電ポートのうち、移動コストが一番小さいものを選ぶ。
    /// </summary>
    /// <param name="ports">充電ポート（充電ピンのある受け渡しパネルと待機パネル）。</param>
    /// <param name="isTakenByOther">ほかの AGV が使っている（予約している）か。</param>
    /// <param name="costOf">AGV の今の位置から充電ポートまでの移動コスト。経路が見つからなければ null。</param>
    /// <returns>選んだ充電ポート。行ける充電ポートがなければ null。</returns>
    public static PanelId? Choose(
        IEnumerable<PanelId> ports,
        Func<PanelId, bool> isTakenByOther,
        Func<PanelId, TimeSpan?> costOf)
    {
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(isTakenByOther);
        ArgumentNullException.ThrowIfNull(costOf);

        return ports
            .Where(port => !isTakenByOther(port))
            .Select(port => (Port: port, Cost: costOf(port)))
            .Where(pair => pair.Cost is not null)
            .OrderBy(pair => pair.Cost)
            .Select(pair => (PanelId?)pair.Port)
            .FirstOrDefault();
    }
}
