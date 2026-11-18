using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FlowHub.Api.Database;

/// <summary>
/// コンテキストごとの DB の設定と、起動するときの準備。
/// </summary>
/// <remarks>
/// PostgreSQL では、コンテキストごとにスキーマを分け、マイグレーションの履歴もそのスキーマに置く。
/// SQLite にはスキーマがないので、コンテキストごとに別のファイル（スキーマ名.db）にする。
/// </remarks>
internal static class Databases
{
    private const string MigrationsHistoryTable = "__EFMigrationsHistory";

    /// <summary>
    /// コンテキストの DB を設定する。
    /// </summary>
    /// <param name="schema">コンテキストのスキーマ名（SQLite ではファイル名）。</param>
    public static void Configure(DbContextOptionsBuilder builder, DatabaseOptions options, string schema)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        switch (options.Provider)
        {
            case DatabaseProvider.PostgreSql:
                if (string.IsNullOrWhiteSpace(options.ConnectionString))
                {
                    throw new InvalidOperationException("PostgreSQL の接続文字列（Database:ConnectionString）が設定されていません。");
                }

                builder.UseNpgsql(options.ConnectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, schema));
                break;

            case DatabaseProvider.Sqlite:
                builder.UseSqlite($"Data Source={Path.Combine(options.SqliteDirectory, schema + ".db")}");

                // SQLite はスキーマを無視する（ファイルを分けているので、スキーマは要らない）
                builder.ConfigureWarnings(warnings => warnings.Ignore(SqliteEventId.SchemaConfiguredWarning));
                break;

            default:
                throw new InvalidOperationException($"知らない DB の種類です：{options.Provider}");
        }
    }

    /// <summary>
    /// 起動するときに、DB を使える状態にする。PostgreSQL はマイグレーションを当て、SQLite は今のモデルからテーブルを作る。
    /// </summary>
    public static Task InitializeAsync(DbContext context, DatabaseOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Provider == DatabaseProvider.Sqlite)
        {
            Directory.CreateDirectory(options.SqliteDirectory);
            return context.Database.EnsureCreatedAsync(cancellationToken);
        }

        return context.Database.MigrateAsync(cancellationToken);
    }
}
