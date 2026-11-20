# C13. 割り当てたあとの搬送の取りやめ

AGV を割り当てたあとの搬送を、AGV へのコマンドの単位で取りやめる流れです。積み込んだあとに取りやめたときは、AGV が持っているものを、必要な装置へ届けるか、ステーションへ返します。

- 根拠：[domain.md](../domain.md)「搬送の流れ」「判断のやり直し」、`src/FlowHub.Dispatching/Domain/Transport.cs`、`src/FlowHub.ToolPlanning/Application/ToolPlanningService.cs`
- 段階ごとの取りやめの結果は [B5. 搬送の状態遷移](05-transport-state.md) にあります。

## 取りやめから、持っているものの行き先を決めるまで

```mermaid
sequenceDiagram
    autonumber
    participant Who as ユーザー・搬送計画
    participant Tool as 搬送計画
    participant Cons as 消耗品の管理
    participant Disp as 配車
    participant Traf as 交通整理
    participant AGV as AGV

    Who->>Disp: 搬送を取りやめる
    Note over Who: 搬送計画が取りやめるのは、判断をやり直して<br/>要求がまるごと要らなくなったときだけ

    alt 走っている（搬送元へ・搬送先へ移動）
        Disp->>Traf: AGV を止める
        Traf->>Traf: まだ渡していないコマンドを取り下げる
        AGV->>Traf: 受け取った区間を走り終えたところで止まる（通路の途中のこともある）
        Disp-->>Who: 取りやめた
    else 受け渡しの途中
        Disp-->>Who: 取りやめ中（キャリアを上げたところで取りやめる）
        Note over Disp,AGV: 許可の前なら許可を出さずに上げる。許可のあとなら受け渡しを終えて上げる
        Traf-->>Disp: CarrierRaised
    else 搬送先で許可を出したあと
        Disp-->>Who: 完了にする（ものは搬送先に届く）
    end

    Traf->>Traf: 止まったパネル以外の予約を解放する
    Disp-->>Tool: TransportCanceled（AGV、積んだまま止まったか）
    Tool->>Cons: 届ける予約・回収の予約を解く
    Note over Tool: 積んだまま止まったなら、その AGV を「持っている」として覚える

    Note over Tool: 判断のやり直し（運転状態がスタートのとき）
    Tool->>Cons: AGV が持っているものを聞く
    Tool->>Cons: 装置ごとに、不足・回収・空きを評価する
    Note over Tool: 不足を補うときは、AGV が持っているものを<br/>ステーションの在庫より先に使う

    alt どこかの装置で必要
        Tool->>Disp: AGV 自身を搬送元にした搬送を頼む<br/>（搬送先 = 必要時刻が一番早い装置、必要時刻 = その装置の不足の必要時刻）
        Note over Tool,Disp: 一部だけ必要なら「降ろしたあとも積んだまま」を付ける
    else どこでも必要でない
        Tool->>Disp: AGV 自身を搬送元にした搬送を頼む<br/>（搬送先 = ステーション、必要時刻 = 返すと決めた時刻）
    end

    Note over Disp: その AGV にだけ割り当てる（オートで待機中、運転状態がスタートのとき）<br/>積み込みを飛ばして、搬送先へ向かう
    Disp->>Traf: AGV を搬送先のパネルへ動かす
```

## 持っているものの行き先

```mermaid
flowchart TD
    held(["積んだまま止まった AGV"]) --> eval{"不足の判断をやり直した結果<br/>どこかの装置で必要か"}
    eval -- "必要（複数の装置で必要なら<br/>必要時刻が一番早い装置だけ）" --> part{"持っているものが<br/>全部必要か"}
    part -- "全部" --> deliverAll["その装置へ届ける"]
    part -- "一部" --> deliverPart["必要なものだけを装置へ届ける<br/>（降ろしたあとも積んだまま）"]
    deliverPart -- "降ろしたら、残りを" --> ret
    eval -- "必要でない" --> ret["ステーションへ返す"]
    ret --> which{"返すステーション"}
    which --> w1["その消耗品を出したステーション<br/>（わからなければ AGV から一番近いステーション）"]
    deliverAll --> free(["降ろしたら、ほかの搬送を割り当てられる"])
    w1 --> free
```

- 返すステーションの選び方は、ステーションの選び方のルール（未決事項 #5）が決まるまでの扱いです。
- 持っているものを届けるか返すまで、その AGV にほかの搬送は割り当てません。割り当て停止の間も、その AGV へは割り当てません。
- その AGV を搬送元にした搬送は、1つだけ受け付けます。積んだまま止まっていない AGV を搬送元にした頼みは、受け付けません。
- 運転状態が停止の間も、取りやめは受け付けます（取りやめに要るコマンドは、停止中でも出します）。
