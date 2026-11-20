# C10. 消耗品を届ける流れ（全体）

装置で不足が起きてから、ステーションの消耗品が装置に届くまでの、コンテキストをまたぐ流れです。回収するときも同じ流れで、搬送元と搬送先が入れ替わります（装置で積み込み、ステーションで降ろします）。

- 根拠：[domain.md](../domain.md)「装置のストッカー」「判断のやり直し」「搬送の要求のまとめ方」「AGV の割り当て」「搬送の流れ」「受け渡しの手順」、各コンテキストの `Application` の層
- 実線の矢印は呼び出し、点線の矢印はイベントと応答です。AGV とのやり取りは [C11](11-agv-communication.md)、受け渡しの細かい手順は [C12](12-handover.md) にあります。

```mermaid
sequenceDiagram
    autonumber
    participant Dev as 装置
    participant Cons as 消耗品の管理
    participant Tool as 搬送計画
    participant Disp as 配車
    participant Traf as 交通整理
    participant AGV as AGV
    participant Sta as ステーション

    Note over Dev,Cons: 不足が起きる（例：消耗品を加工部に取り込んだ）
    Dev->>Cons: 加工部に取り込んだ（ID）
    Cons->>Cons: 中古品にする（新品の数が減る）
    Cons-->>Tool: StockChanged

    Note over Tool: 判断のやり直し（スタートのときだけ）
    Tool->>Cons: 不足・回収・空きスロットを評価する
    Tool->>Cons: ステーションの在庫を聞く（先入れ先出し）
    Tool->>Tool: 同じ装置・同じ加工の種類を、キャリアに入る個数までまとめる<br/>必要時刻 = 不足を見つけた時刻
    Tool->>Cons: 届ける予約を付ける
    Tool->>Disp: 搬送を頼む（搬送元 = ステーション、搬送先 = 装置、必要時刻）
    Disp-->>Tool: 搬送 ID

    Note over Disp: 待ち行列（必要時刻が早い順、同じなら頼まれた順）
    Disp->>Traf: 候補の AGV ごとに、搬送元までの移動コストを聞く
    Disp->>Disp: 搬送元に近い AGV を割り当てる<br/>（同じなら累計の走行距離が短い、それも同じなら ID の順）
    Disp->>Traf: AGV を搬送元のパネルへ動かす（必要時刻を添える）
    Traf->>Traf: 全台の経路を計画し、区間ごとに予約できたら指示を作る
    Traf->>AGV: 動作コマンド（GET の応答で渡す）
    AGV->>Traf: 完了の通知（パネルに着いた）
    Traf-->>Disp: AgvArrived

    rect rgb(240, 245, 255)
        Note over Disp,Sta: 搬送元（ステーション）で積み込む（C12）
        Disp->>Traf: キャリアを下げる
        Traf-->>Disp: CarrierLowered
        Disp-->>Cons: TransferPermissionRequested（積み込み）
        Cons->>Sta: 搬送の許可（出す消耗品の ID）
        Sta->>Cons: 受け渡しの完了（出した ID）
        Cons->>Cons: 所在を AGV 内部にする
        Cons->>Disp: 受け渡しの完了を伝える
        Cons-->>Tool: StockChanged
        Disp->>Traf: キャリアを上げる
        Traf-->>Disp: CarrierRaised
    end

    Disp->>Traf: AGV を搬送先のパネルへ動かす
    Traf->>AGV: 動作コマンド
    AGV->>Traf: 完了の通知
    Traf-->>Disp: AgvArrived

    rect rgb(240, 255, 240)
        Note over Dev,Disp: 搬送先（装置）で降ろす（C12）
        Disp->>Traf: キャリアを下げる
        Traf-->>Disp: CarrierLowered
        Disp-->>Cons: TransferPermissionRequested（降ろし）
        Cons->>Cons: 入れ先のスロットを決める（空いているうち番号が一番小さいもの）
        Cons->>Dev: 搬送の許可（消耗品の ID ごとの入れ先のスロット）
        Dev->>Cons: 受け渡しの完了（入れた ID）
        Cons->>Cons: 所在を受け渡し部（入れ先のスロット付き）にする
        Cons->>Disp: 受け渡しの完了を伝える
        Cons-->>Tool: StockChanged
        Disp->>Traf: キャリアを上げる
        Traf-->>Disp: CarrierRaised
    end

    Disp->>Disp: 搬送を完了にする（AGV は搬送先にとどまり、待機中に戻る）
    Disp-->>Tool: TransportCompleted
    Disp->>Disp: 待ち行列の搬送の割り当てをやり直す
```

## 補足

- ステーションに必要な数の一部しかないときは、あるぶんを先に届け、残りは上位システムに補充を頼みます（頼む数 = 足りない数 − まだ入庫されていない数。1以上のときだけ）。
- ストッカーに空きがないときは、空きの数までしか届ける要求を出さず、残りは回収が終わってから出します。
- 判断をやり直すたびに、**割り当て前の要求はすべていったん取りやめて出し直します**（必要時刻は保ちます）。割り当てたあとの要求は、まるごと要らなくなったときだけ取りやめます（[C13](13-cancel-after-assignment.md)）。
- 空いているスロットが降ろす数より足りないときは、許可を出さずにエラーにし、画面で知らせます（搬送は受け渡し待ちのまま止まります）。
- 受け渡し部からストッカーへ入ったことの知らせは、まだ決まっていません（未決事項 #23）。
