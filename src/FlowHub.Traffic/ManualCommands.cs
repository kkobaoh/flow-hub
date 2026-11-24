namespace FlowHub.Traffic;

/// <summary>
/// マニュアルで行き先を指定したときの結果。
/// </summary>
public enum ManualMoveResult
{
    /// <summary>走らせ始めた。</summary>
    Started,

    /// <summary>すでにそのパネルにいた。</summary>
    AlreadyThere,

    /// <summary>マップの上に経路がないので、受け付けなかった（画面で知らせる）。</summary>
    NoRoute,

    /// <summary>今はほかの AGV に塞がれていて行けないので、受け付けなかった（画面で知らせる）。</summary>
    Blocked,

    /// <summary>特殊消耗品受け渡しパネルで、キャリアを下ろす角度と違う「着いたときの向き」を指定されたので、受け付けなかった（画面で知らせる）。</summary>
    ArrivalHeadingNotAllowed,
}

/// <summary>
/// マニュアルの1つずつの動作。
/// </summary>
public abstract record ManualStep;

/// <summary>向いている方向の隣のパネルへ前進する。</summary>
public sealed record ManualForward : ManualStep;

/// <summary>向いている方向の逆の隣のパネルへ後退する。</summary>
public sealed record ManualBackward : ManualStep;

/// <summary>ラインを読み取る回転（オートと同じ）。回転後の絶対角度は 0・90・180・270度だけ。</summary>
public sealed record ManualRotate(int Heading) : ManualStep;

/// <summary>角度を指定した回転。ラインを読まずに、回転後の絶対角度（0〜359度）まで回る。</summary>
public sealed record ManualFreeRotate(int Heading) : ManualStep;

/// <summary>
/// マニュアルの1つずつの動作を送ったときの結果。
/// </summary>
public enum ManualStepResult
{
    /// <summary>受け付けた（すぐに渡したか、前の動作のあとに並べた）。</summary>
    Accepted,

    /// <summary>すぐに渡せるのに、動く先のパネルの予約が取れないので、受け付けなかった（画面で知らせる）。</summary>
    Blocked,

    /// <summary>マップの決まりを守れない動作なので、受け付けなかった（画面で知らせる）。</summary>
    NotAllowed,
}
