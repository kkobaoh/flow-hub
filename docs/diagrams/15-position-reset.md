# C15. AGV が再起動したとき・位置リセット

AGV が再起動したときや、異常（位置がずれた可能性がある）になったときに、ユーザーが位置リセットで復旧させる流れです。

- 根拠：[domain.md](../domain.md)「AGV からの完了の通知」「AGV のエラー」「位置のリセット」「まだ AGV に渡していないコマンドの取り下げ」「コントロールステータス」「全台の経路の計画」
- AGV との通信の細かい形は [C11. AGV との通信](11-agv-communication.md) にあります。

## 異常から位置リセットで戻るまで

```mermaid
sequenceDiagram
    autonumber
    participant User as ユーザー
    participant Traf as 交通整理
    participant AGV as AGV
    participant Disp as 配車

    alt AGV が再起動した
        AGV->>Traf: GET position
        Traf-->>AGV: 最後に把握していた位置
        AGV->>Traf: GET commands?lastReceived=-1
    else 位置がずれた可能性のあるエラー
        AGV->>Traf: POST error crossMissed／crossFalselyDetected
    else 食い違う通知・順番が飛んだ通知
        AGV->>Traf: POST moved（指示と違うパネル）
        Traf->>AGV: 緊急停止を渡す
    end

    Traf->>Traf: 異常にする（予約は保つ。まだ渡していないコマンドを取り下げる）
    Traf->>Traf: 止まった AGV を動かない障害物として、全台の計画をすぐ立て直す<br/>（行き先と残りの経路は捨てる）
    Traf-->>Disp: AgvFaulted
    Disp->>Disp: 搬送を中断する（TransportInterrupted を公開。画面で知らせる）<br/>充電ポートへ向かっていたなら「充電ポートを探す」に戻す

    User->>User: 現場で確かめ、AGV を手で十字の上まで動かす
    User->>Traf: 位置リセット（パネルと絶対角度）

    alt AGV が動作中（走行中、キャリアの上げ下げ中）
        Traf-->>User: 受け付けない（先に緊急停止してください）
    else 指定したパネルをほかの AGV が予約している
        Traf-->>User: 受け付けない（画面で知らせる）
    else 受け付けた
        Traf->>Traf: 指定のパネルを予約し、ほかの予約と残りの経路を捨てる<br/>キャリアの状態は保つ（上げ下げの途中なら、下がっているとする）
        Traf->>AGV: positionReset（パネル、角度）
        Note over AGV: 自分でアラームをクリアし、持っている指示を捨てる
        AGV->>Traf: POST positionResetCompleted
        Traf->>Traf: 待機中にする
        Traf-->>Disp: AgvPositionResetCompleted
        Disp->>Disp: AGV が空いた → 待ち行列の割り当てをやり直す<br/>充電ポートを探していたなら、もう一度探す
    end
```

- 位置リセットの完了の通知を受けるまでは、新しい動作コマンドを渡しません。
- 「異常」はアラームクリアでは戻れません。システムが AGV の本当の位置を見失っているため、予約していないパネルに AGV がいて、ほかの AGV とぶつかるおそれがあるからです。
- 異常のときに届いた完了の通知は、番号と中身が指示と合っていれば受け付けて位置を更新しますが、異常は解除しません。後ろのパネルの予約も解放しません。
- 中断した搬送を、位置リセットのあとにやり直すか取りやめるかを選ぶ仕組みは、まだ実装していません（[backlog.md](../backlog.md) #8、未決事項 #10）。

## 切り離しから戻すとき

```mermaid
sequenceDiagram
    participant User as ユーザー
    participant Traf as 交通整理
    participant AGV as AGV

    Note over Traf: 切り離し中の AGV には、どのコマンドも送らない
    User->>Traf: コントロールステータスを切り替える（切り替え先と、位置を指定）
    alt 指定したパネルをほかの AGV が予約している
        Traf-->>User: 受け付けない（切り離しのまま）
    else 受け付けた
        Traf->>AGV: positionReset（パネル、角度）
        AGV->>Traf: POST positionResetCompleted
        Traf->>Traf: 切り替え先のステータスにする
    end
```
