namespace FlowHub.Traffic.Domain;

/// <summary>
/// AGV への走行の指示の1つ。AGV との通信の形式（未定）に変換して送る。
/// </summary>
internal abstract record TravelCommand;

/// <summary>
/// 前進。距離は次の十字のラインまで。
/// </summary>
internal sealed record ForwardCommand(int DistanceMm) : TravelCommand;

/// <summary>
/// 後退。距離は次の十字のラインまで。
/// </summary>
internal sealed record BackwardCommand(int DistanceMm) : TravelCommand;

/// <summary>
/// その場での回転。回転後の絶対角度を送り、どちら回りかは AGV が判断する。
/// </summary>
internal sealed record RotateCommand(int Heading) : TravelCommand;
