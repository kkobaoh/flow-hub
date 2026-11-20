# C14. 充電

充電残量の通知から、充電ポートへ向かい、割り当てに戻るまでの流れです。充電に行かせるかの判断は配車が、充電ポートまで動かすのは交通整理が受け持ちます。

- 根拠：[domain.md](../domain.md)「充電」「AGV の割り当て」、`src/FlowHub.Dispatching/Domain/AgvCharging.cs`
- 充電の状態の遷移は [B9. AGV の状態](09-agv-state.md) にあります。

## 充電ポートへ向かう

```mermaid
sequenceDiagram
    autonumber
    participant AGV as AGV
    participant Traf as 交通整理
    participant Disp as 配車

    AGV->>Traf: POST battery percent=18
    Traf-->>Disp: AgvBatteryChanged（18）

    alt 搬送の途中
        Disp->>Disp: 搬送を終えてから充電する（届けることを優先）
        Note over Disp: 搬送を終えたパネルに充電ピンがあれば、その場で充電する
    else 充電ピンのあるパネルにいる
        Disp->>Disp: その場で充電する（動かない）
    else それ以外（待機中、運転状態がスタート、オートか割り当て停止）
        Disp->>Disp: 新しい搬送を割り当てない
        loop 移動コストが近い充電ポートから
            Disp->>Traf: 移動コストを聞く、予約されているかを聞く
        end
        alt 行ける、空いている充電ポートがある
            Disp->>Traf: AGV を充電ポートへ動かす
            Traf->>AGV: 動作コマンド
            AGV->>Traf: 完了の通知
            Traf-->>Disp: AgvArrived
            Disp->>Disp: 充電中にする
        else ない
            Disp->>Disp: その場で待つ
            Disp-->>Disp: NoChargingPortAvailable を公開する（画面で知らせる）
        end
    end
```

- ほかの AGV が使っている（予約している）充電ポートと、ほかの AGV が向かっている充電ポートは、使っているものとみなします。
- 充電ポートは、充電ピンを設置した受け渡しパネルか待機パネルです。

## 充電ポートが空いたとき

```mermaid
sequenceDiagram
    participant Traf as 交通整理
    participant Disp as 配車

    Note over Traf: 充電ポートにいた AGV が離れて、予約を外した
    Traf-->>Disp: ChargingPortReleased（パネル）
    Disp->>Disp: 待っている AGV を並べる<br/>（充電残量が少ない順 → 空いた充電ポートに近い順 → AGV の ID の順）
    loop 待っている AGV ごとに
        Disp->>Disp: もう一度、充電ポートを探させる
    end
```

## 割り当てに戻る

```mermaid
sequenceDiagram
    participant AGV as AGV
    participant Traf as 交通整理
    participant Disp as 配車

    AGV->>Traf: POST battery percent=80
    Traf-->>Disp: AgvBatteryChanged（80）
    Disp->>Disp: 割り当てに戻す値以上 → 通常に戻す<br/>（充電ポートにとどまり、待機中に戻る）
    Disp->>Disp: 待ち行列の搬送の割り当てをやり直す
```

## 決まりごと

| 項目 | 内容 |
|---|---|
| 充電に向かう値 | 既定 20%。これを **下回ったら** 向かう（ちょうどなら割り当てる） |
| 割り当てに戻す値 | 既定 80%。これ **以上** に回復したら戻す |
| 値の持ち方 | システム全体の既定値と、AGV の種類ごとの値（省略したら、登録したときの既定値で埋めて保存）。割り当てに戻す値は、充電に向かう値以上 |
| 通知が来ていない AGV | 充電は足りているものとみなす |
| 向かう途中で回復した | 向かうのはやめず、着いてから判断する |
| 向かう途中で異常になった | 「充電ポートを探す」に戻し、位置リセットが完了して待機中になったら、もう一度探す |
| 運転状態が停止 | 向かわせない。スタートしたときに探させる |
| マニュアル・ロック・切り離し | 向かわせない。オートか割り当て停止に戻したときに探させる |
