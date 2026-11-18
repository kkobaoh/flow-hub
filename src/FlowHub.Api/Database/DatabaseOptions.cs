namespace FlowHub.Api.Database;

/// <summary>
/// 使う DB の種類。
/// </summary>
public enum DatabaseProvider
{
    /// <summary>本番。コンテキストごとにスキーマを分ける。</summary>
    PostgreSql,

    /// <summary>テストとデバッグ。コンテキストごとに別のファイルにする。</summary>
    Sqlite,
}

/// <summary>
/// DB の設定（appsettings の「Database」）。
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>使う DB の種類。</summary>
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.PostgreSql;

    /// <summary>PostgreSQL の接続文字列。</summary>
    public string? ConnectionString { get; set; }

    /// <summary>SQLite のファイルを置くフォルダー。コンテキストごとに「スキーマ名.db」のファイルを作る。</summary>
    public string SqliteDirectory { get; set; } = "data";
}
