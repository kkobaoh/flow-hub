using FlowHub.Map;

namespace FlowHub.Traffic.Domain;

/// <summary>
/// AGV への走行の指示の1つ。AGV との通信の形式（未定）に変換して送る。
/// </summary>
internal abstract record TravelCommand;

/// <summary>
/// 前進。距離は次の十字のラインまで。AGV は移動先のパネルから、今いるパネルを推測する。
/// </summary>
/// <param name="Special">特殊な前進か（特殊消耗品受け渡しパネルを出発するか、そこに着く前進）。</param>
internal sealed record ForwardCommand(int DistanceMm, PanelId To, bool Special = false) : TravelCommand;

/// <summary>
/// 後退。距離は次の十字のラインまで。AGV は移動先のパネルから、今いるパネルを推測する。
/// </summary>
/// <param name="Special">特殊な後退か（特殊消耗品受け渡しパネルを出発するか、そこに着く後退）。</param>
internal sealed record BackwardCommand(int DistanceMm, PanelId To, bool Special = false) : TravelCommand;

/// <summary>
/// その場での回転。回転後の絶対角度を送り、どちら回りかは AGV が判断する。
/// </summary>
internal sealed record RotateCommand(int Heading) : TravelCommand;

/// <summary>
/// 角度を指定した、その場での回転（マニュアルで使う）。ラインを読まずに、回転後の絶対角度まで回る。
/// </summary>
internal sealed record FreeRotateCommand(int Heading) : TravelCommand;

/// <summary>
/// 特殊消耗品受け渡しパネルでの、その場での回転（キャリアを下ろす角度へ回るときなど）。回転後の絶対角度まで回る。
/// </summary>
internal sealed record RotateSpecialCommand(int Heading) : TravelCommand;
