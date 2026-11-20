# D16. ドメインモデル（クラス図）

ドメインの概念と、その関係です。コンテキストごとに分けて描いています。クラスの名前は、コードの型の名前に合わせています（行は DB のテーブルではなく、概念です。DB は [D18](18-db-schema.md) にあります）。

- 根拠：[domain.md](../domain.md)「消耗品」「消耗品の搬送方式」「ステーション」「装置のストッカー」「AGV の割り当て」「充電」「パネルとマップ」、各コンテキストの公開の型

## 消耗品の管理

```mermaid
classDiagram
    direction LR

    class ProcessType["加工の種類（ProcessTypeSettings）"] {
        ID
        キャリアの種類
        キャリアに入る個数
        搬送方式（プレート搬送／専用 AGV 搬送）
    }
    class Variety["品種（VarietyId）"] {
        ID
    }
    class Consumable["消耗品（ConsumableView）"] {
        ID（1個ずつ）
        新品か中古品か（ConsumableCondition）
        所在（ConsumableLocation）
        要確認
    }
    class Plate["プレート（将来）"] {
        所在
    }
    class Device["装置（DeviceLayout）"] {
        ID
        受け渡しパネル
    }
    class Stocker["ストッカー"] {
        番号（1から）
        スロットの数（0ならスロットなし）
    }
    class Slot["スロット（SlotRef）"] {
        ストッカーの番号
        スロットの番号
    }
    class StockerSetting["ストッカーの設定"] {
        品種ごとの個数
    }
    class Station["ステーション（StationLayout）"] {
        ID
        受け渡しパネル
    }
    class ExpectedArrival["入庫の予定"] {
        消耗品の ID
        品種
    }

    ProcessType "1" <-- "*" Variety : 属する
    Variety "0..1" <-- "*" Consumable : 品種（未登録の ID ならわからない）
    Plate "0..1" o-- "*" Consumable : 載っている（プレート搬送）
    ProcessType "1" <-- "*" Plate : 専用
    Device "1" *-- "*" Stocker
    Stocker "1" *-- "*" Slot
    Device "1" -- "0..1" StockerSetting : ユーザーが設定する
    StockerSetting "*" --> "*" Variety
    Consumable ..> Slot : 所在がストッカーならそのスロット
    Consumable ..> Station : 所在がステーションならそのステーション
    ExpectedArrival --> Variety
```

- 互換性は **品種の中だけ** です。同じ品種なら、どの ID の消耗品を送ってもかまいません。
- 1つのスロットに置けるのは1個です。プレートに載ったまま置く装置のストッカーには、スロットがありません。
- 入庫の予定（ID と品種の対応）は、上位システムか人が前もって登録します。ステーションで読み取った ID がここにあれば、その品種の新品として入庫します。

## 配車

```mermaid
classDiagram
    direction LR

    class TransportRequest["搬送の要求（TransportOrder）"] {
        要求元
        運ぶものの参照（配車には中身のわからない値）
        搬送元（パネルと受け渡しの相手）
        搬送先（パネルと受け渡しの相手）
        必要なキャリアの種類
        必要時刻
    }
    class AgvTransportOrder["AGV 自身を搬送元にした要求（AgvTransportOrder）"] {
        積んだ搬送（LoadedBy）
        降ろしたあとも積んだままか
    }
    class Transport["搬送（Transport）"] {
        搬送 ID
        段階（TransportStage）
        割り当てた AGV
        一時停止の理由
    }
    class DispatchAgv["AGV（配車の側）"] {
        AGV の ID
        キャリアの種類（AGV に固定）
    }
    class DispatchAgvType["AGV の種類（配車の側）"] {
        充電に向かう値
        割り当てに戻す値
    }
    class AgvCharging["充電（AgvCharging）"] {
        状態（ChargingState）
        充電残量
        向かう充電ポート
    }

    TransportRequest <|-- AgvTransportOrder
    Transport "1" --> "1" TransportRequest
    Transport "*" --> "0..1" DispatchAgv : 割り当て
    DispatchAgv "*" --> "1" DispatchAgvType
    DispatchAgv "1" -- "1" AgvCharging
```

- 配車は、運ぶもの（消耗品やワーク）を知りません。運ぶものは、要求元が渡す参照（文字列）としてだけ持ちます。
- AGV は、交通整理（速さ）と配車（キャリアの種類、充電の設定値）の両方に登録します。

## 交通整理とマップ

```mermaid
classDiagram
    direction LR

    class TrafficAgv["AGV（AgvView）"] {
        AGV の ID（AgvId）
        位置と向き（Pose）
        走行の状態（AgvState）
        キャリアが上がっているか
        累計の走行距離
        コントロールステータス
    }
    class TrafficAgvType["AGV の種類（交通整理の側）"] {
        前進速度
        後退速度
        回転速度
        特殊消耗品受け渡しパネルを走れるか
    }
    class MapSnapshot["マップの版（MapSnapshot）"] {
        版の番号
    }
    class Panel["パネル（Panel）"] {
        パネルの ID（PanelId）
        種類（PanelType）
        充電ピンがあるか
        キャリアを下ろす角度（特殊消耗品受け渡しパネルだけ）
    }
    class Connection["つながり（Connection）"] {
        十字の間の距離
        向き（絶対角度）
        一方通行か
    }
    class CarrierHeight["キャリアを下げる高さ"] {
        高さ
        設定した時刻
    }
    class Reservation["パネルの予約"] {
        AGV
        パネル
    }

    TrafficAgv "*" --> "1" TrafficAgvType
    MapSnapshot "1" *-- "*" Panel
    MapSnapshot "1" *-- "*" Connection
    Connection "*" --> "2" Panel : つなぐ
    CarrierHeight "0..1" --> "1" Panel : 受け渡しパネルごと（版の外）
    Reservation "*" --> "1" Panel
    Reservation "*" --> "1" TrafficAgv
    TrafficAgv ..> Panel : 位置
```

- マップは、変更できないスナップショット（版）として公開します。キャリアを下げる高さは、たびたび変わるので版の外に持ちます。
- 1枚のパネルを予約できるのは、1台の AGV だけです。

## コンテキストをまたぐ参照

| 参照する側 | 参照するもの | 持ち方 |
|---|---|---|
| 消耗品の管理（装置、ステーション） | マップのパネル | `PanelId` |
| 消耗品の管理（所在が AGV 内部） | 配車の搬送 | `TransportId` |
| 搬送の要求 | 受け渡しの相手（装置・ステーション） | 中身のわからない文字列（`Endpoint`） |
| 配車 | 交通整理の AGV | `AgvId` |
