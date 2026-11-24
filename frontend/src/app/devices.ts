import type { EquipmentId } from '../map/equipment';
import type { PanelId } from '../map/model';
import { byIdNumber } from './listing';

/** 装置の中にある1個の消耗品。tone は、回収を頼んだもの（collect）や、食い違いでふさいでいるもの（blocked）を目立たせるため。 */
export interface HeldConsumable {
  /** 消耗品の ID（わからないときは null）。 */
  readonly id: string | null;
  readonly variety: string;
  /** 状態の文（新品、中古品・回収を頼んだ など）。 */
  readonly note: string;
  readonly tone: 'normal' | 'collect' | 'blocked';
}

/** スロットの中身。空き、届ける途中などで空けておく（reserved）、消耗品がある。 */
export type SlotContent =
  | { readonly kind: 'empty' }
  | { readonly kind: 'reserved'; readonly consumable: HeldConsumable }
  | { readonly kind: 'held'; readonly consumable: HeldConsumable };

export interface Slot {
  readonly number: number;
  readonly content: SlotContent;
}

/** ストッカー。スロットがあるものと、プレートに載せたまま置くもの。 */
export type Stocker =
  | { readonly name: string; readonly layout: 'slots'; readonly slots: readonly Slot[] }
  | { readonly name: string; readonly layout: 'plate'; readonly items: readonly HeldConsumable[] };

/** 品種ごとの設定と様子。status はサーバーの判断（足りている、足りない、設定より多い、設定にない）。 */
export interface SettingRow {
  readonly variety: string;
  /** 設定の個数（設定にない品種は 0）。 */
  readonly target: number;
  /** ある新品（届ける途中を含む）。 */
  readonly have: number;
  readonly status: 'enough' | 'short' | 'excess' | 'notInSetting';
}

/** 装置とストッカーの画面に出す、1台の装置の中。 */
export interface DeviceInside {
  readonly id: EquipmentId;
  /** 受け渡しパネル。 */
  readonly panel: PanelId;
  /** 加工部にあるもの。 */
  readonly processing: HeldConsumable | null;
  readonly stockers: readonly Stocker[];
  /** 受け渡し部にあるもの。 */
  readonly handover: HeldConsumable | null;
  readonly setting: readonly SettingRow[];
}

/** 編集中の設定の1行（個数は、入力のままの文字）。 */
export interface DraftRow {
  readonly variety: string;
  readonly target: string;
}

export interface SavedRow {
  readonly variety: string;
  readonly target: number;
}

/** 出す装置：指定した装置があればそれ、なければ番号がいちばん小さい装置（docs/screens.md）。 */
export function chooseDevice(devices: readonly DeviceInside[], wanted: string | null): DeviceInside | null {
  const found = devices.find((d) => d.id === wanted);
  if (found) {
    return found;
  }
  return [...devices].sort((a, b) => byIdNumber(a.id, b.id))[0] ?? null;
}

/** 置ける個数の上限（すべてのスロットの数）。プレートに載せたまま置くストッカーがあれば、上限なし（null）。 */
export function slotCapacity(device: DeviceInside): number | null {
  let total = 0;
  for (const s of device.stockers) {
    if (s.layout === 'plate') {
      return null;
    }
    total += s.slots.length;
  }
  return total;
}

const COUNT = /^\d+$/;

/** 設定を保存できない理由（保存できるなら null）。個数は 0 以上の整数で、合計はスロットの数まで。 */
export function settingProblem(draft: readonly DraftRow[], capacity: number | null): string | null {
  const bad = draft.find((r) => !COUNT.test(r.target.trim()));
  if (bad) {
    return `${bad.variety} の個数を、0 以上の整数で入れてください。`;
  }
  const total = draft.reduce((sum, r) => sum + Number(r.target), 0);
  if (capacity !== null && total > capacity) {
    return `合計 ${total}個が、スロットの数（${capacity}）を超えています。`;
  }
  return null;
}

/** 保存する設定。0個の品種は、設定から外す。 */
export function toSaved(draft: readonly DraftRow[]): SavedRow[] {
  return draft.map((r) => ({ variety: r.variety, target: Number(r.target) })).filter((r) => r.target > 0);
}

/** 編集を始めるときの下書き（いま設定にある品種だけ）。 */
export function draftOf(setting: readonly SettingRow[]): DraftRow[] {
  return setting.filter((r) => r.target > 0).map((r) => ({ variety: r.variety, target: String(r.target) }));
}
