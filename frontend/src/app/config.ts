import { byIdNumber } from './listing';

/** 運び方（domain.md の「消耗品の搬送方式」）。 */
export type CarryMethod = 'plate' | 'dedicated';

export const carryMethodLabel: Record<CarryMethod, string> = {
  plate: 'プレート搬送（兼用 AGV）',
  dedicated: '専用 AGV 搬送',
};

export interface ProcessKind {
  readonly name: string;
  readonly method: CarryMethod;
  readonly carrier: string;
  /** キャリアに入る個数。 */
  readonly capacity: number;
}

export interface Variety {
  readonly name: string;
  readonly processKind: string;
  /** この品種に対応させた ID。 */
  readonly ids: readonly string[];
  /** そのうち、在庫にある ID（外せない）。 */
  readonly stockedIds: readonly string[];
  /** 消耗品の記録かストッカーの設定で使っているか。 */
  readonly inUse: boolean;
}

/** 装置のストッカーの置き方。スロットがあるもの（ストッカーごとのスロットの数）と、プレートに載せたまま置くもの。 */
export type DeviceLayout = { readonly kind: 'slots'; readonly slots: readonly number[] } | { readonly kind: 'plate'; readonly stockers: number };

export interface DeviceConfig {
  readonly id: string;
  readonly panel: string;
  readonly layout: DeviceLayout;
  /** ストッカーごとの、消耗品があるスロットの番号（プレートのときは、消耗品があるストッカーだけ要素を持つ）。 */
  readonly occupied: readonly (readonly number[])[];
  /** 中に消耗品があるか、かかわる搬送がある。 */
  readonly busy: boolean;
}

export interface StationConfig {
  readonly id: string;
  readonly panel: string;
  /** 中に消耗品があるか、かかわる搬送がある。 */
  readonly busy: boolean;
}

export interface ProcessKindDraft {
  readonly name: string;
  readonly method: CarryMethod | '';
  readonly carrier: string;
  readonly capacity: string;
}

export interface VarietyDraft {
  readonly name: string;
  readonly processKind: string;
}

export interface DeviceDraft {
  readonly id: string;
  readonly panel: string;
  readonly plate: boolean;
  /** スロットがあるときの、ストッカーごとのスロットの数。 */
  readonly slots: readonly string[];
  /** プレートのときの、ストッカーの数。 */
  readonly plateStockers: string;
}

const POSITIVE = /^\d+$/;
const positive = (text: string) => POSITIVE.test(text.trim()) && Number(text) >= 1;

function nameProblem(name: string, label: string, existing: readonly string[], original: string | null): string | null {
  const n = name.trim();
  if (n === '') {
    return `${label}を入れてください。`;
  }
  if (existing.includes(n) && n !== original) {
    return `${n} は、もう登録してあります。`;
  }
  return null;
}

/** 加工の種類を保存できない理由。original は直す前の名前（加えるときは null）。 */
export function processKindProblem(draft: ProcessKindDraft, existing: readonly string[], original: string | null): string | null {
  return (
    nameProblem(draft.name, '加工の種類の名前', existing, original) ??
    (draft.method === '' ? '運び方を選んでください。' : null) ??
    (draft.carrier === '' ? 'キャリアの種類を選んでください。' : null) ??
    (positive(draft.capacity) ? null : 'キャリアに入る個数を、1 以上の整数で入れてください。')
  );
}

/** 加工の種類を消すのを押せない理由。その加工の種類の品種がなければ消せる。 */
export function processKindDeleteBlockedReason(name: string, varieties: readonly Variety[]): string | null {
  const users = varieties
    .filter((v) => v.processKind === name)
    .map((v) => v.name)
    .sort(byIdNumber);
  return users.length === 0 ? null : `${users.join('、')} の加工の種類なので、消せません。`;
}

/** 品種を保存できない理由。original は直す前の品種（加えるときは null）。加工の種類は、使われていないときだけ変えられる。 */
export function varietyProblem(draft: VarietyDraft, existing: readonly string[], original: Variety | null): string | null {
  return (
    nameProblem(draft.name, '品種の名前', existing, original?.name ?? null) ??
    (draft.processKind === '' ? '加工の種類を選んでください。' : null) ??
    (original !== null && original.inUse && original.processKind !== draft.processKind ? `${original.name} は使われているので、加工の種類を変えられません。` : null)
  );
}

export function varietyDeleteBlockedReason(variety: Variety): string | null {
  return variety.inUse ? `${variety.name} は、消耗品の記録かストッカーの設定で使っているので、消せません。` : null;
}

/** ID を品種に加えられない理由。どの品種にもまだ対応させていない ID だけ加えられる。 */
export function idProblem(id: string, varieties: readonly Variety[]): string | null {
  const i = id.trim();
  if (i === '') {
    return 'ID を入れてください。';
  }
  const owner = varieties.find((v) => v.ids.includes(i));
  return owner ? `${i} は、もう ${owner.name} に対応させてあります。` : null;
}

/** ID を外すのを押せない理由。在庫にある ID は外せない。 */
export function removeIdBlockedReason(variety: Variety, id: string): string | null {
  return variety.stockedIds.includes(id) ? `${id} は在庫にあるので、外せません。` : null;
}

export function deviceDraftOf(device: DeviceConfig): DeviceDraft {
  return device.layout.kind === 'slots'
    ? { id: device.id, panel: device.panel, plate: false, slots: device.layout.slots.map(String), plateStockers: '1' }
    : { id: device.id, panel: device.panel, plate: true, slots: ['1'], plateStockers: String(device.layout.stockers) };
}

export function toDeviceLayout(draft: DeviceDraft): DeviceLayout {
  return draft.plate ? { kind: 'plate', stockers: Number(draft.plateStockers) } : { kind: 'slots', slots: draft.slots.map(Number) };
}

/** 装置を保存できない理由。original は直す前の装置（加えるときは null）。減らすのは、減るぶんが空のときだけ。 */
export function deviceProblem(draft: DeviceDraft, existing: readonly string[], original: DeviceConfig | null): string | null {
  const basic =
    nameProblem(draft.id, '装置の ID ', existing, original?.id ?? null) ??
    (draft.panel === '' ? '受け渡しパネルを選んでください。' : null) ??
    (draft.plate ? (positive(draft.plateStockers) ? null : 'ストッカーの数を、1 以上の整数で入れてください。') : null) ??
    (!draft.plate && draft.slots.length === 0 ? 'ストッカーを 1 つ以上加えてください。' : null);
  if (basic !== null) {
    return basic;
  }
  if (!draft.plate) {
    const bad = draft.slots.findIndex((s) => !positive(s));
    if (bad >= 0) {
      return `ストッカー${bad + 1} のスロットの数を、1 以上の整数で入れてください。`;
    }
  }
  if (original === null) {
    return null;
  }
  if (original.busy && draft.panel !== original.panel) {
    return `${original.id} は使われているので、受け渡しパネルを変えられません。`;
  }
  if (original.busy && draft.plate !== (original.layout.kind === 'plate')) {
    return `${original.id} は使われているので、置き方を変えられません。`;
  }
  const layout = toDeviceLayout(draft);
  const kept = layout.kind === 'slots' ? layout.slots.length : layout.stockers;
  for (const [i, used] of original.occupied.entries()) {
    if (used.length === 0) {
      continue;
    }
    if (i >= kept) {
      return `ストッカー${i + 1} に消耗品があるので、減らせません。`;
    }
    const count = layout.kind === 'slots' ? (layout.slots[i] ?? 0) : Infinity;
    const over = used.filter((n) => n > count).sort((a, b) => a - b)[0];
    if (over !== undefined) {
      return `ストッカー${i + 1} のスロット${over} に消耗品があるので、減らせません。`;
    }
  }
  return null;
}

export function deviceDeleteBlockedReason(device: DeviceConfig): string | null {
  return device.busy ? `${device.id} は、中に消耗品があるか、かかわる搬送があるので、消せません。` : null;
}

export function stationDeleteBlockedReason(station: StationConfig): string | null {
  return station.busy ? `${station.id} は、中に消耗品があるか、かかわる搬送があるので、消せません。` : null;
}

/** 選べる受け渡しパネル：まだどこにも使われていないもの。直すときは、いまのパネルも。 */
export function panelChoices(transferPanels: readonly string[], used: readonly string[], current: string | null): string[] {
  return transferPanels.filter((p) => p === current || !used.includes(p));
}

/** ステーションを保存できない理由。受け渡しパネルは、使われていないときだけ変えられる。 */
export function stationProblem(draft: { readonly id: string; readonly panel: string }, existing: readonly string[], original: StationConfig | null): string | null {
  return (
    nameProblem(draft.id, 'ステーションの ID ', existing, original?.id ?? null) ??
    (draft.panel === '' ? '受け渡しパネルを選んでください。' : null) ??
    (original !== null && original.busy && draft.panel !== original.panel ? `${original.id} は使われているので、受け渡しパネルを変えられません。` : null)
  );
}
