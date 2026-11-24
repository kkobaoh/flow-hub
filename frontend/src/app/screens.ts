/** 画面の一覧（docs/screens.md の「画面の一覧」と同じ）。 */
export type ScreenKey =
  | 'map'
  | 'transports'
  | 'devices'
  | 'station'
  | 'consumables'
  | 'mapEdit'
  | 'agvs'
  | 'consumableConfig'
  | 'users';

export interface ScreenEntry {
  readonly key: ScreenKey;
  readonly title: string;
  /** 「設定」の見出しの下に並べるか（構成を変える画面）。 */
  readonly setting: boolean;
}

export const screens: readonly ScreenEntry[] = [
  { key: 'map', title: '全体の様子（マップ）', setting: false },
  { key: 'transports', title: '搬送の一覧', setting: false },
  { key: 'devices', title: '装置とストッカー', setting: false },
  { key: 'station', title: 'ステーションの在庫', setting: false },
  { key: 'consumables', title: '消耗品の検索と手入力', setting: false },
  { key: 'mapEdit', title: 'マップの編集', setting: true },
  { key: 'agvs', title: 'AGV と AGV の種類の登録', setting: true },
  { key: 'consumableConfig', title: '消耗品の構成', setting: true },
  { key: 'users', title: 'ユーザーとロールの管理', setting: true },
];
