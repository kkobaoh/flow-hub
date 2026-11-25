namespace FlowHub.Map;

/// <summary>キャリアの高さの出どころ。</summary>
public enum CarrierHeightSource
{
    /// <summary>AGV が測った。</summary>
    Measured,

    /// <summary>人が画面から入れた。</summary>
    Manual,
}

/// <summary>受け渡しパネルのキャリアを下げる高さの設定。</summary>
/// <param name="HeightMm">高さ（mm。1 以上）。</param>
/// <param name="SetAt">設定した日時。</param>
public sealed record CarrierHeightSetting(int HeightMm, CarrierHeightSource Source, DateTimeOffset SetAt);

/// <summary>人が画面からキャリアの高さを入れた結果。</summary>
public enum CarrierHeightSaveResult
{
    /// <summary>保存した（空にしたときも、これを返す）。</summary>
    Saved,

    /// <summary>公開中の版の受け渡しパネルではない（保存していない）。</summary>
    NotTransferPanel,

    /// <summary>まだマップを公開していない（保存していない）。</summary>
    NoMap,
}
