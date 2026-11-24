namespace FlowHub.Map;

/// <summary>
/// パネルの種類。
/// </summary>
public enum PanelType
{
    /// <summary>十字のラインがある、走行用のパネル。</summary>
    Normal,

    /// <summary>装置やステーションと AGV の間で、ものを受け渡すパネル。行き止まり。</summary>
    Transfer,

    /// <summary>待避するためのパネル。行き止まり。</summary>
    Standby,

    /// <summary>
    /// 特殊消耗品受け渡しパネル。特殊な受け渡し部で、ものを受け渡す。通過でき（行き止まりではない）、中で回転できる。
    /// 専用の AGV しか走れず、キャリアを下ろす角度が決まっている。充電ピンは設置できない。
    /// </summary>
    SpecialTransfer,
}

/// <summary>
/// パネル。
/// </summary>
/// <param name="Id">パネルの ID。</param>
/// <param name="Type">パネルの種類。</param>
/// <param name="HasChargingPin">充電ピンを設置しているか。受け渡しパネルと待機パネルにだけ設置できる。</param>
public sealed record Panel(PanelId Id, PanelType Type, bool HasChargingPin)
{
    /// <summary>キャリアを下ろす角度（特殊消耗品受け渡しパネルだけ。ほかの種類は null）。</summary>
    public Heading? CarrierLoweringHeading { get; init; }

    /// <summary>
    /// 行き止まりのパネル（前進でのみ入れて、後退でのみ出られる。中では回転できない）か。
    /// </summary>
    public bool IsDeadEnd => Type is PanelType.Transfer or PanelType.Standby;
}
