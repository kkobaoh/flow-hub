using FlowHub.Api.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FlowHub.IntegrationTests;

/// <summary>
/// ローカルの PostgreSQL へのつなぎ先。接続文字列はユーザーシークレットから読み、環境変数があればそちらを優先する（README.md を参照）。
/// </summary>
internal static class LocalPostgres
{
    private const string ConnectionStringKey = "Postgres:ConnectionString";

    /// <summary>
    /// 本番と同じ PostgreSQL の設定。接続文字列が設定されていなければ、設定のしかたを添えて失敗させる。
    /// </summary>
    public static DatabaseOptions Options()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(LocalPostgres).Assembly)
            .AddEnvironmentVariables()
            .Build();
        var connectionString = configuration[ConnectionStringKey];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"ローカルの PostgreSQL の接続文字列（{ConnectionStringKey}）が設定されていません。tests/FlowHub.IntegrationTests/README.md を参照して、ユーザーシークレットに設定してください。");
        }

        return new DatabaseOptions { Provider = DatabaseProvider.PostgreSql, ConnectionString = connectionString };
    }

    /// <summary>
    /// コンテキストのスキーマを消して、まっさらにする（前に流したテストの中身を残さないため）。マイグレーションの履歴もスキーマの中にあるので、一緒に消える。
    /// </summary>
    public static async Task DropSchemaAsync(DatabaseOptions options, string schema, CancellationToken cancellationToken)
    {
        // スキーマ名は SQL のパラメーターにできないので、英小文字と下線だけであることを確かめてから埋め込む
        if (schema.Length == 0 || !schema.All(c => c is (>= 'a' and <= 'z') or '_'))
        {
            throw new ArgumentException($"スキーマ名として使えない文字があります：{schema}", nameof(schema));
        }

        var builder = new DbContextOptionsBuilder();
        Databases.Configure(builder, options, schema);
        await using var context = new DbContext(builder.Options);
        await context.Database.ExecuteSqlRawAsync(string.Concat("DROP SCHEMA IF EXISTS ", schema, " CASCADE"), cancellationToken);
    }
}
