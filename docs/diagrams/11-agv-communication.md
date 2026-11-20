# C11. AGV との通信

システム（交通整理）と AGV の間のやり取りです。AGV がシステムへリクエストを送るだけで、システムから AGV へ直接送る経路はありません。

- 根拠：[agv-protocol.md](../agv-protocol.md)、[domain.md](../domain.md)「動き方と走行の指示」「AGV との通信（コマンドの受け渡し）」「AGV からの完了の通知」「指示の番号」「予期しない通知の扱い」「動作コマンドの受信の確認」「緊急停止」「まだ AGV に渡していないコマンドの取り下げ」
- 窓口：`src/FlowHub.Traffic/Infrastructure/AgvEndpoints.cs`

## 区間ごとの走行（ふだんの流れ）

例：A → B → C と前進し、90度へ回転してから、D へ前進する経路。区間は回転で区切ります。

```mermaid
sequenceDiagram
    participant AGV as AGV
    participant Sys as システム（交通整理）

    AGV->>Sys: GET /agv/{id}/commands?lastReceived=11
    Note over Sys: 渡すコマンドがなければ、25秒待つ
    Note over Sys: 区間1（A→B→C、90度へ回転）の<br/>パネルがすべて自分の番で、予約できた
    Sys-->>AGV: 200 commands：[12 motion（forward B、forward C、rotate 90）]
    AGV->>Sys: GET commands?lastReceived=12
    Note over Sys: 12 まで受け取ったと判断する
    AGV->>Sys: POST notifications：motionReceived 12
    Note over Sys: 受信の完了の通知は、ログに残すだけ

    AGV->>Sys: POST moved 12 forward B
    Note over Sys: 位置を B にする。A の予約を解放する
    AGV->>Sys: POST moved 12 forward C
    Note over Sys: 位置を C にする。B の予約を解放する
    AGV->>Sys: POST rotated 12 heading 90
    Note over Sys: 回転を終えたので、次の区間の指示を作る<br/>（予約できたときだけ）
    Sys-->>AGV: 200 commands：[13 motion（forward D）]（待っていた GET の応答）
    AGV->>Sys: GET commands?lastReceived=13
    AGV->>Sys: POST moved 13 forward D
    Note over Sys: 経路を走り終えた。今いるパネルだけを予約した状態にする<br/>（AgvArrived を公開する）

    Sys-->>AGV: 25秒たってもコマンドがない → 200 commands：[]
    AGV->>Sys: GET commands?lastReceived=13
```

- 前進・後退の1つ1つは、**次の十字のラインまでの距離と、移動先のパネル** です。AGV はマップを持たず、この情報と十字の検出から、今いるパネルを推測します。
- 番号は AGV ごとに1つずつ増やし、すべてのコマンド（動作、緊急停止、アラームクリア、位置リセット、キャリアの上げ下げ）で1つの通し番号を共有します。システムを再起動しても続きから数えます（DB の `traffic.command_numbers`）。

## 受け取ったかの確認（応答が失われたとき）

```mermaid
sequenceDiagram
    participant AGV as AGV
    participant Sys as システム（交通整理）

    AGV->>Sys: GET commands?lastReceived=12
    Sys--xAGV: 200 commands：[13 motion …]（応答が途中で失われた）
    AGV->>Sys: GET commands?lastReceived=12
    Note over Sys: 13 をまだ受け取っていない → 同じ動作コマンドをもう一度渡す
    Sys-->>AGV: 200 commands：[13 motion …]
    AGV->>Sys: GET commands?lastReceived=13
    Note over Sys: 受け取った。受信の完了の通知が届かなくても、もう一度は渡さない<br/>（今の AGV は2回めを無視しないので、2回渡すと同じ区間を2回走る）
```

## 緊急停止とアラームクリア

```mermaid
sequenceDiagram
    participant User as ユーザー
    participant Sys as システム（交通整理）
    participant AGV as AGV

    User->>Sys: 緊急停止
    Sys->>Sys: まだ渡していない動作コマンドとキャリアのコマンドを取り下げる
    AGV->>Sys: GET commands?lastReceived=13
    Sys-->>AGV: 200 commands：[14 emergencyStop]
    Note over AGV: 止まり、アラームを発報する<br/>（完了の通知はない）
    AGV->>Sys: POST error serverRequestedStop
    Note over Sys: 緊急停止中にする<br/>止まる直前に終えていた動作の完了の通知は、受け付けて位置を更新する<br/>2枚のパネルの間なら、両側のパネルを使用中とする

    User->>Sys: アラームクリア（画面から）
    AGV->>Sys: GET commands?lastReceived=14
    Sys-->>AGV: 200 commands：[15 alarmClear]
    Note over AGV: 止まった地点から、残りの指示の続きを再開する
    AGV->>Sys: POST alarmCleared 15
    Note over Sys: アラームクリアの完了を受けてから、<br/>取り下げた区間を新しい番号で渡し直す
    Sys-->>AGV: 200 commands：[16 motion …]
```

## AGV が再起動したとき

```mermaid
sequenceDiagram
    participant AGV as AGV
    participant Sys as システム（交通整理）
    participant User as ユーザー

    Note over AGV: 再起動して、指示・位置・番号を忘れる
    AGV->>Sys: GET /agv/{id}/position
    Sys-->>AGV: 200 { panel, heading }（最後に把握していた位置。わからなければ 204）
    AGV->>Sys: GET commands?lastReceived=-1
    Note over Sys: 走りかけの動作コマンドは渡し直さない<br/>まだ渡していない緊急停止・アラームクリアは捨てる<br/>予約は保ったまま、異常と同じく位置リセットを待つ
    User->>User: AGV を手で十字の上まで動かす
    User->>Sys: 位置リセット（パネルと絶対角度）
    Sys-->>AGV: 200 commands：[17 positionReset { panel, heading }]
    Note over AGV: 自分でアラームをクリアし、持っている指示を捨てる
    AGV->>Sys: POST positionResetCompleted 17
    Note over Sys: 新しい動作コマンドを渡すのは、この完了の通知を受けてから
```

## 予期しない通知の扱い

| 種類 | 見分け方 | 扱い |
|---|---|---|
| 重複して届いた通知 | 直前に受け付けた通知と、番号・動作の種類・パネルの ID がすべて同じ | 無視してログに残す（応答は 204） |
| 古い通知 | 位置リセットで捨てた指示の通知、走り終えた動作コマンドの番号 | 無視してログに残す |
| 実際の状態と食い違う通知 | 今の指示の番号なのに、指示と違うパネルに着いた | **異常** にする（緊急停止を送り、位置リセットを待つ） |
| 順番が飛んだ通知 | 次に着くはずのパネルではなく、その先の予定のパネル | **異常** にする |
| 意味のわからない通知 | 送っていない番号、知らない種類、壊れた内容 | 受け付けずにログに残す（壊れた内容は 400） |
| 予期しないアラームクリア・位置リセットの完了 | そのコマンドを送っていない | 無視して、ログに残し、画面で警告する |
