import { byIdNumber } from '../../shared/utils/listing';

/** 加工の種類。今回はプレート搬送を使わないので、運び方は持たない（使うときに足す）。 */
export interface ProcessKind {
  readonly name: string;
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

/** 装置の構成。今回はプレート搬送を使わないので、プレートに載せたまま置く装置は扱わない（使うときに足す）。 */
export interface DeviceConfig {
  readonly id: string;
  readonly panel: string;
  /** ストッカーごとのスロットの数。 */
  readonly slots: readonly number[];
  /** ストッカーごとの、ふさがっているスロットの番号（消耗品がある、届ける入れ先に決めた、加工中の消耗品の元のスロット）。 */
  readonly occupied: readonly (readonly number[])[];
  /** 中に消耗品があるか、かかわる搬送（届ける予約、指名、受け渡しの途中）がある。 */
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
  /** ストッカーごとのスロットの数。 */
  readonly slots: readonly string[];
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
    (draft.carrier.trim() === '' ? 'キャリアの種類を入れてください。' : null) ??
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
  return { id: device.id, panel: device.panel, slots: device.slots.map(String) };
}

/** 装置を保存できない理由。original は直す前の装置（加えるときは null）。減らすのは、減るぶんが空のときだけ。 */
export function deviceProblem(draft: DeviceDraft, existing: readonly string[], original: DeviceConfig | null): string | null {
  const basic =
    nameProblem(draft.id, '装置の ID ', existing, original?.id ?? null) ??
    (draft.panel === '' ? '受け渡しパネルを選んでください。' : null) ??
    (draft.slots.length === 0 ? 'ストッカーを 1 つ以上加えてください。' : null);
  if (basic !== null) {
    return basic;
  }
  const bad = draft.slots.findIndex((s) => !positive(s));
  if (bad >= 0) {
    return `ストッカー${bad + 1} のスロットの数を、1 以上の整数で入れてください。`;
  }
  if (original === null) {
    return null;
  }
  if (original.busy && draft.panel !== original.panel) {
    return `${original.id} は使われているので、受け渡しパネルを変えられません。`;
  }
  const slots = draft.slots.map(Number);
  for (const [i, used] of original.occupied.entries()) {
    if (used.length === 0) {
      continue;
    }
    if (i >= slots.length) {
      return `ストッカー${i + 1} に消耗品があるので、減らせません。`;
    }
    const count = slots[i] ?? 0;
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

/** ステーションを加えるのを押せない理由。ステーションの選び方のルールが決まるまでは、1台だけ。 */
export function stationAddBlockedReason(stations: readonly StationConfig[]): string | null {
  return stations.length === 0 ? null : 'ステーションは、1台だけ登録できます（ステーションの選び方のルールが決まるまで）。';
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
