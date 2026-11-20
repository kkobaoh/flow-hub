# E20. 配置図（本番・デバッグ・テスト）

本番、開発とデバッグ（シミュレーター）、テストで、何をどうつなぐかの違いです。

- 根拠：[CLAUDE.md](../../CLAUDE.md)「外部とのやり取りと時刻」「ルール」、`src/FlowHub.Api/appsettings*.json`、`src/FlowHub.Api/Database/DatabaseOptions.cs`、`tests/FlowHub.IntegrationTests/README.md`

## 本番

```mermaid
flowchart LR
    subgraph factory["工場内の閉じたネットワーク"]
        agv["AGV<br/>（複数台）"]
        device["装置"]
        station["ステーション"]
        upstream["上位システム<br/>（ない構成もある）"]
        browser["画面（ブラウザー）<br/>React + TypeScript（これから）"]

        subgraph server["サーバー"]
            api["FlowHub.Api<br/>（ASP.NET、.NET 10）<br/>起動したら、いつも停止から始める"]
            pg[("PostgreSQL<br/>スキーマ：map、traffic、<br/>dispatching、consumables")]
        end
    end

    agv -- "HTTP（認証なし）<br/>/agv/{agvId}/..." --> api
    browser -- "HTTP<br/>/system、/transports …" --> api
    api -- "方式は未定（未決事項 #11）" --> device
    api -- "方式は未定（未決事項 #11）" --> station
    api -- "方式は未定（未決事項 #11）" --> upstream
    api -- "EF Core（Npgsql）<br/>マイグレーションを当てる" --> pg
```

- 上は目指す形です。装置・ステーション・上位システムとの通信の方式が決まっていないので、**今の組み立て（`FlowHubSystem`）は、本番の設定でもシミュレーターをつないでいます**。方式が決まったら、本物との通信の実装に差し替えます。

## 開発・デバッグ（シミュレーター）

```mermaid
flowchart LR
    agvsim["AGV（シミュレーター、<br/>または agv-protocol.md に合わせた AGV）"]

    subgraph dev["開発の PC"]
        subgraph proc["FlowHub.Api のプロセス"]
            api["FlowHub.Api<br/>起動したらすぐスタート（設定）<br/>初期データ（initial-data.json）を入れる"]
            sim["FlowHub.Simulator<br/>装置・ステーション・上位システムの仮の実装<br/>（頼まれたら、待ち時間のあとに完了を通知）"]
        end
        sqlite[("SQLite<br/>data/map.db、traffic.db、<br/>dispatching.db、consumables.db")]
    end

    agvsim -- "HTTP /agv/{agvId}/..." --> api
    api --- sim
    api -- "EF Core<br/>EnsureCreated（今のモデルからテーブルを作る）" --> sqlite
```

- シミュレーター用のデータが本番のデータと混ざらないよう、DB の接続先は環境ごとに分けます。
- シミュレーターの待ち時間（装置・ステーションの受け渡し、出庫、補充）は、`Simulator` の設定で変えます。

## テスト

```mermaid
flowchart LR
    subgraph normal["ふだんの dotnet test（PostgreSQL なしで軽く回す）"]
        unit["各コンテキストのテスト<br/>FakeTimeProvider、偽物・InMemory の実装"]
        arch["アーキテクチャテスト<br/>（コンテキスト・層・イベントの決まり）"]
        dbt["DB を使うテスト"] --> sq[("SQLite")]
    end

    subgraph integ["統合テスト（push の前、DB の部分を変えたとき）"]
        it["tests/FlowHub.IntegrationTests<br/>dotnet test --project tests/FlowHub.IntegrationTests<br/>-p:RunIntegrationTests=true"] --> lpg[("ローカルの PostgreSQL<br/>接続文字列はユーザーシークレット")]
    end
```

## 環境ごとの違い

| 項目 | 本番 | 開発・デバッグ | テスト |
|---|---|---|---|
| DB | PostgreSQL（コンテキストごとにスキーマ） | SQLite（コンテキストごとにファイル） | SQLite（統合テストだけ PostgreSQL） |
| テーブルの作り方 | マイグレーション | `EnsureCreated` | `EnsureCreated`（統合テストはマイグレーション） |
| 装置・ステーション・上位システム | 本物との通信（方式は未定） | シミュレーター | 偽物 |
| 現在時刻 | `TimeProvider.System` | `TimeProvider.System` | `FakeTimeProvider` |
| 起動したときの運転状態 | 停止 | スタート（設定） | テストごと |
| 初期データ | － | `InitialData/initial-data.json` | テストごと |
