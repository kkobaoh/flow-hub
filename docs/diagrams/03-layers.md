# A3. コンテキストの中の層とポート（層の図）

コンテキストの中の `Domain`、`Application`、`Infrastructure` と、外部とのやり取りを通すポート（インターフェース）の関係です。

- 根拠：[CLAUDE.md](../../CLAUDE.md)「クリーンアーキテクチャ」「外部とのやり取りと時刻」「ルール」、`tests/FlowHub.ArchitectureTests/LayerAndEventRulesTests.cs`

## 層の依存の向き

```mermaid
flowchart LR
    subgraph ctx["1つのコンテキスト（例：FlowHub.Consumables）"]
        direction LR
        pub["公開インターフェースと DTO<br/>（public。ほかのコンテキストが使う）<br/>例：IDeviceGateway、ConsumableId、StockChanged"]
        infra["Infrastructure<br/>DbContext（internal）、ストア、HTTP の窓口"]
        app["Application<br/>サービス、購読者（Subscriptions.cs）<br/>イベントの公開はここだけ"]
        dom["Domain<br/>ビジネスルール<br/>（フレームワーク・DB・通信を知らない）"]
    end

    infra --> app
    infra --> dom
    app --> dom
    app --> pub
    infra -. "ポートを実装" .-> pub
```

- 矢印は「使う側 → 使われる側」です。内側（Domain）は外側（Application、Infrastructure）を知りません。
- アーキテクチャテストで守らせていること：
  - Domain の層は、Application と Infrastructure の層を使わない
  - Application の層は、Infrastructure の層を使わない
  - イベントを公開できるのは Application の層だけ

## ポートと、差し替える実装

外部とのやり取りは、すべてポートを通します。本番・シミュレーター・テストで、`FlowHub.Api` の組み立てのときに実装を差し替えます。

```mermaid
flowchart LR
    subgraph inner["コンテキストの内側（ポートを定義）"]
        dg["IDeviceGateway<br/>（装置）"]
        sg["IStationGateway<br/>（ステーション）"]
        ug["IUpstreamGateway<br/>（上位システム）"]
        cp["IConsumablesPersistence"]
        ms["IMapStore"]
        cs["ICommandNumberStore<br/>IControlStatusStore"]
        tp["TimeProvider<br/>（現在時刻）"]
    end

    subgraph prod["本番"]
        real["本物との通信<br/>（方式は未定：未決事項 #11）"]
        db["DB のストア<br/>ConsumablesStore、DbMapStore、TrafficStore<br/>（PostgreSQL）"]
        sys["TimeProvider.System"]
    end

    subgraph simu["シミュレーター・デバッグ"]
        es["EquipmentSimulator<br/>（頼まれたら、待ち時間のあとに完了を通知）"]
        dbs["DB のストア（SQLite）"]
    end

    subgraph test["テスト"]
        fake["偽物・InMemory の実装<br/>InMemoryMapStore、InMemoryCommandNumberStore、<br/>InMemoryControlStatusStore、NoConsumablesPersistence"]
        ftp["FakeTimeProvider<br/>（時間を早送り・止める）"]
    end

    real -.-> dg
    real -.-> sg
    real -.-> ug
    es -.-> dg
    es -.-> sg
    es -.-> ug
    db -.-> cp
    db -.-> ms
    db -.-> cs
    dbs -.-> cp
    dbs -.-> ms
    dbs -.-> cs
    fake -.-> ms
    fake -.-> cs
    fake -.-> cp
    sys -.-> tp
    ftp -.-> tp
```

## 入ってくる側の窓口

| 窓口 | 置き場所 | 内容 |
|---|---|---|
| AGV との通信（`/agv/{agvId}/...`） | `FlowHub.Traffic/Infrastructure/AgvEndpoints.cs` | コマンドのロングポーリング、位置の取得、通知（[agv-protocol.md](../agv-protocol.md)） |
| 運転状態（`/system/...`） | `FlowHub.Api/Composition/SystemEndpoints.cs` | 状態の取得、スタート、停止 |
| 搬送の取りやめ（`/transports/{id}/cancel`） | `FlowHub.Api/Composition/TransportEndpoints.cs` | 割り当てたあとの搬送の取りやめ |
| 装置・ステーションからの通知 | 今は `FlowHub.Simulator` が `ConsumablesService` を直接呼ぶ | 本物との通信の方式が決まったら、窓口を作る（未決事項 #11） |
