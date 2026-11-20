# C12. 受け渡しの手順

受け渡しパネルで、AGV と装置（またはステーション）がものを受け渡す手順です。届けるときと回収するときで手順は同じで、ものの向き（キャリアから相手へ、相手からキャリアへ）が逆になるだけです。

- 根拠：[domain.md](../domain.md)「受け渡しの手順」「装置のストッカー」、`src/FlowHub.Dispatching/Domain/Transport.cs`、`src/FlowHub.Consumables/Gateways.cs`

```mermaid
sequenceDiagram
    autonumber
    participant Disp as 配車
    participant Traf as 交通整理
    participant AGV as AGV
    participant Cons as 消耗品の管理
    participant Peer as 装置・ステーション

    Note over Disp: AGV が受け渡しパネルに着いた（AgvArrived）
    Disp->>Traf: キャリアを下げる
    Note over Traf: 受け渡しパネルにいて、待機中で、キャリアが上がっているときだけ<br/>高さはマップの設定（受け渡しパネルごと）から読む
    alt 高さが設定されている
        Traf->>AGV: lowerCarrier（heightMm）
    else 高さが設定されていない
        Traf->>AGV: lowerCarrier（heightMm なし）
        AGV->>Traf: carrierHeightMeasured（測った高さ）
        Traf->>Traf: 高さをマップの設定に保存する
    end

    AGV->>Traf: carrierLowered
    Traf-->>Disp: CarrierLowered
    Disp-->>Cons: TransferPermissionRequested（搬送 ID、運ぶもの、相手、積み込みか降ろしか）

    alt 装置で降ろす
        Cons->>Cons: 入れ先のスロットを決める
        alt 空いているスロットが足りない
            Cons->>Cons: 許可を出さずにエラーにし、画面で知らせる（受け渡し待ちのまま止まる）
        else 足りる
            Cons->>Peer: 搬送の許可（ID ごとの入れ先のスロット）
        end
    else 装置から積み込む（回収）
        alt 回収するものが記録上ストッカーにない
            Cons->>Cons: 許可を出さずにエラーにし、画面で知らせる
        else ある
            Cons->>Peer: 搬送の許可（取り出すスロット）
        end
    else ステーション
        Cons->>Peer: 搬送の許可（出し入れする ID）
    end

    Peer->>Peer: ものをキャリアに出し入れする
    Peer->>Cons: 受け渡しの完了（出し入れした ID）
    Cons->>Cons: 所在を更新する<br/>（頼んだ ID と違ったら、通知の ID を正とし、警告する）
    Cons->>Disp: 受け渡しの完了を伝える
    Disp->>Traf: キャリアを上げる
    Traf->>AGV: raiseCarrier
    AGV->>Traf: carrierRaised
    Traf-->>Disp: CarrierRaised
    Note over Disp: 搬送元なら「積み込んだ」→ 搬送先へ<br/>搬送先なら「降ろした」→ 完了
```

## 途中で止まったとき

| 起きたこと | 扱い |
|---|---|
| キャリアを下げている間に緊急停止・エラー | AGV はその場で止まる。ユーザーはキャリアを上げられない。アラームクリアのあと、システムが上げるコマンドを送る（取り下げたキャリアのコマンドは、新しい番号で渡し直す） |
| 上げ下げの途中でエラー | AGV はその場で止まる。アラームクリアで途中から再開する |
| 異常（位置リセットが必要）のとき | アラームクリアはできないので、位置リセットのあとに上げる（位置リセットでキャリアの状態は保たれる） |
| 搬送が一時停止中（緊急停止中、運転状態が停止） | 届いた知らせで段階は進めるが、次のコマンドも、受け渡しの相手への搬送の許可も出さない（AGV が止まっている間に、装置がものを出し入れしないようにするため） |
| 装置・ステーションが受け渡しを終えない | 待ち続ける（時間を区切るかは未決事項 #10「異常時の扱い」で決める） |
| 受け渡しの途中で取りやめ | 許可をまだ出していなければ、許可を出さずに上げる。出したあとなら、受け渡しを最後まで終えて上げる（搬送先なら完了として扱う） |

- キャリアを下げている間は、走行できません。システムはキャリアが上がるまで AGV を走らせません。
- 特殊消耗品受け渡しパネルでは、キャリアを下ろす角度を向いているときだけ下げられます（搬送で向かうときは、着いたら `rotate_special` でその角度へ回ってから止まります）。
