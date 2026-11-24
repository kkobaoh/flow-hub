namespace FlowHub.Map;

/// <summary>
/// 隣り合う2枚のパネルのつながり（ライン）。
/// </summary>
/// <param name="From">つながりの一方の端のパネル。</param>
/// <param name="To">つながりのもう一方の端のパネル。</param>
/// <param name="DistanceMm">十字と十字の間の距離（mm）。</param>
/// <param name="Heading">From から To へ進むときの向き（絶対角度）。To から From へは、その逆向き。</param>
/// <param name="OneWay">一方通行か。true のときは From から To へだけ進める。</param>
public sealed record Connection(PanelId From, PanelId To, int DistanceMm, Heading Heading, bool OneWay);
