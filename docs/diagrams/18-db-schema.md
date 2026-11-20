# D18. DB のスキーマ（ER 図）

コンテキストごとの DB のテーブルです。PostgreSQL ではコンテキストごとにスキーマを分け、SQLite ではコンテキストごとに別のファイル（`スキーマ名.db`）にします。ほかのコンテキストのテーブルは直接読みません。

- 根拠：[CLAUDE.md](../../CLAUDE.md)「ルール」、各コンテキストの `Infrastructure/*DbContext.cs`
- 今 DB に保存しているのは、**構成のデータ**（マップ、AGV と種類、装置・ステーション・品種など）と、再起動しても残す必要のあるもの（コマンドの番号、コントロールステータス）だけです。消耗品の在庫と所在、搬送、予約、運転状態はメモリーに持ち、再起動すると消えます（再起動したときの状態の復元は、未決事項 #14「非機能要件」）。
- 図の中のテーブル名は「スキーマ名_テーブル名」で書いています（例：`map_panels` は、スキーマ `map` のテーブル `panels`）。列の名前は、DB の実際の名前（プロパティの名前と同じ）です。

## map（マップ）

```mermaid
erDiagram
    map_versions ||--o{ map_panels : "版のパネル"
    map_versions ||--o{ map_connections : "版のつながり"
    map_versions {
        int Version PK "公開した版の番号"
    }
    map_panels {
        int Version PK "版"
        string PanelId PK "パネルの ID"
        int Order "版の中での並び順"
        string Type "パネルの種類"
        bool HasChargingPin "充電ピンがあるか"
        int CarrierLoweringHeading "キャリアを下ろす角度（特殊消耗品受け渡しパネルだけ）"
    }
    map_connections {
        int Version PK "版"
        int Order PK "版の中での並び順"
        string FromPanelId "つなぐパネル"
        string ToPanelId "つなぐパネル"
        int DistanceMm "十字の間の距離"
        int HeadingDegrees "向き（絶対角度）"
        bool OneWay "一方通行か"
    }
    map_carrier_heights {
        string PanelId PK "受け渡しパネル（版の外）"
        int HeightMm "キャリアを下げる高さ"
        datetime SetAt "設定した時刻（UTC）"
    }
```

## traffic（交通整理）

```mermaid
erDiagram
    traffic_agv_types ||--o{ traffic_agvs : "種類"
    traffic_agv_types {
        string Id PK
        double ForwardSpeedMmPerSecond "前進速度"
        double BackwardSpeedMmPerSecond "後退速度"
        double RotationSpeedDegreesPerSecond "回転速度"
        bool CanTravelSpecialTransfer "特殊消耗品受け渡しパネルを走れるか"
    }
    traffic_agvs {
        string Id PK
        string TypeId FK
        string ControlStatus "コントロールステータス（新規は切り離し）"
    }
    traffic_command_numbers {
        string AgvId PK
        long NextNumber "次に付けるコマンドの番号"
    }
```

## dispatching（配車）

```mermaid
erDiagram
    dispatching_agv_types ||--o{ dispatching_agvs : "種類"
    dispatching_agv_types {
        string Id PK
        int GoChargeBelowPercent "充電に向かう値"
        int ResumeAtPercent "割り当てに戻す値"
    }
    dispatching_agvs {
        string Id PK
        string TypeId FK
        string CarrierType "キャリアの種類（AGV に固定）"
    }
    dispatching_charging_defaults {
        int Id PK "1行だけ"
        int GoChargeBelowPercent "既定の充電に向かう値"
        int ResumeAtPercent "既定の割り当てに戻す値"
    }
```

## consumables（消耗品の管理）

```mermaid
erDiagram
    consumables_devices ||--o{ consumables_device_stockers : "ストッカー"
    consumables_devices ||--o{ consumables_stocker_settings : "ストッカーの設定"
    consumables_process_types ||--o{ consumables_varieties : "品種"
    consumables_varieties ||--o{ consumables_stocker_settings : "設定する品種"
    consumables_varieties ||--o{ consumables_expected_arrivals : "入庫の予定"

    consumables_devices {
        string Id PK
        string TransferPanel "受け渡しパネル（マップのパネルの ID）"
        bool HasStockerSetting "ストッカーの設定をしたか（空の設定も設定あり）"
    }
    consumables_device_stockers {
        string DeviceId PK
        int StockerNumber PK "1から"
        int SlotCount "スロットの数"
    }
    consumables_stations {
        string Id PK
        string TransferPanel "受け渡しパネル"
    }
    consumables_process_types {
        string Id PK
        string CarrierType "キャリアの種類"
        int CarrierCapacity "キャリアに入る個数"
    }
    consumables_varieties {
        string Id PK
        string ProcessTypeId FK
    }
    consumables_stocker_settings {
        string DeviceId PK
        string VarietyId PK
        int Count "ストックしておく個数"
    }
    consumables_expected_arrivals {
        string ConsumableId PK "消耗品の ID"
        string VarietyId FK "品種"
    }
```

## コンテキストをまたぐ値

外部キーは、同じスキーマの中にだけ張ります。ほかのコンテキストのものは、ID の値として持つだけです。

| 列 | 指しているもの |
|---|---|
| `consumables.devices.TransferPanel`、`consumables.stations.TransferPanel` | マップのパネルの ID |
| `traffic.agvs.Id` と `dispatching.agvs.Id` | 同じ AGV（両方に登録する。片方にしかない AGV は、起動したときにログに残して画面で知らせ、両方にある AGV だけで動かす） |
| `dispatching.agvs.CarrierType` と `consumables.process_types.CarrierType` | キャリアの種類 |
