# 統合テスト（ローカルの PostgreSQL）

本番と同じ PostgreSQL で、DB の部分（マイグレーションやテーブルの作り）を確かめるテストです。

ふだんのテストは、軽くたくさん回せるように SQLite で流しています。このプロジェクトだけは、ローカルの PostgreSQL を使います。

## いつ流すか

DB の部分（マイグレーションやテーブルの作り）を変えたときに、**push する前** に流します。

## 準備（最初に一度だけ）

### 1. PostgreSQL を用意する

ローカルに PostgreSQL を入れて、起動しておきます。

統合テスト用の DB（例：`flowhub_integration_tests`）を作っておきます。

```
createdb -U postgres flowhub_integration_tests
```

### 2. 接続文字列をユーザーシークレットに置く

接続文字列にはパスワードが入るので、リポジトリには入れず、**ユーザーシークレット** に置きます。ユーザーシークレットは、リポジトリの外（ユーザーのフォルダー）に保存されます。

雛型は、このディレクトリの [secrets.template.json](secrets.template.json) です。自分の環境に合わせて値を変え、次のように設定します（リポジトリのルートで実行します）。

```
dotnet user-secrets set "Postgres:ConnectionString" "Host=localhost;Port=5432;Database=flowhub_integration_tests;Username=postgres;Password=（自分のパスワード）" --project tests/FlowHub.IntegrationTests
```

設定できたかは、次で確かめられます。

```
dotnet user-secrets list --project tests/FlowHub.IntegrationTests
```

### 環境変数で上書きする

一時的に別の DB で流したいときなどは、環境変数で上書きできます。環境変数があれば、ユーザーシークレットより優先します。

```
# PowerShell
$env:Postgres__ConnectionString = "Host=localhost;Port=5432;Database=other;Username=postgres;Password=..."
```

（設定の名前の区切りの `:` は、環境変数では `__`（下線2つ）にします。）

## 流し方

リポジトリのルートで実行します。

```
dotnet test --project tests/FlowHub.IntegrationTests -p:RunIntegrationTests=true
```

- `-p:RunIntegrationTests=true` を付けないと、このプロジェクトはテストとして扱われません。
- ふだんの `dotnet test`（全テスト）には、このプロジェクトは **含まれません**。PostgreSQL のない環境でも、ふだんのテストは流せます。
