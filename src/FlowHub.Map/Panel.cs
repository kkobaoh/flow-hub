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
}

/// <summary>
/// パネル。
/// </summary>
/// <param name="Id">パネルの ID。</param>
/// <param name="Type">パネルの種類。</param>
/// <param name="HasChargingPin">充電ピンを設置しているか。受け渡しパネルと待機パネルにだけ設置できる。</param>
public sealed record Panel(PanelId Id, PanelType Type, bool HasChargingPin)
{
    /// <summary>
    /// 行き止まりのパネル（前進でのみ入れて、後退でのみ出られる。中では回転できない）か。
    /// </summary>
    public bool IsDeadEnd => Type is PanelType.Transfer or PanelType.Standby;
}
