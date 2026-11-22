/** パーミッション（docs/screens.md の「パーミッションと最初のロール」の各行）。 */
export type Permission =
  | 'operation' // 運転状態のスタート・停止
  | 'emergencyStop' // 緊急停止
  | 'transportCancel' // 搬送の取りやめ、中断した搬送のやり直し・取りやめ
  | 'controlBasic' // コントロールステータス：オート ⇔ 割り当て停止
  | 'stockerSetting' // ストッカーの設定（段取り替え）
  | 'consumableOps' // 要確認の消耗品の修正、出庫、指名、AGV での交換の指示
  | 'controlAdvanced' // コントロールステータス：マニュアル・ロック・切り離し（と、切り離しから戻す）
  | 'recovery' // アラームクリア、位置リセット
  | 'manual' // マニュアル操作
  | 'stockEdit' // 在庫の手入力
  | 'configure' // マップの編集・公開、AGV と AGV の種類の登録、消耗品の構成
  | 'userAdmin'; // ユーザーとロールの管理

export type Permissions = ReadonlySet<Permission>;

const viewer: Permission[] = [];
const operator: Permission[] = [...viewer, 'operation', 'emergencyStop', 'transportCancel', 'controlBasic', 'stockerSetting', 'consumableOps'];
const maintainer: Permission[] = [...operator, 'controlAdvanced', 'recovery', 'manual', 'stockEdit'];
const admin: Permission[] = [...maintainer, 'configure', 'userAdmin'];

/** 最初から用意する4つのロール（Storybook とテストの見本。本物は、サーバーから受け取る）。 */
export const initialRoles = {
  閲覧のみ: new Set(viewer) as Permissions,
  運用: new Set(operator) as Permissions,
  保守: new Set(maintainer) as Permissions,
  管理者: new Set(admin) as Permissions,
} as const;
