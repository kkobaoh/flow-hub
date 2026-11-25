import type { AgvId } from './agv';
import { controlStatusLabel, type ControlStatus } from './controlStatus';
import { byIdNumber } from '../../shared/utils/listing';
import type { Permissions } from '../auth/permissions';

/** AGV の種類。速さと充電の値はいつも具体的な数（加えるときに省いた充電の値は、既定値で埋めてある）。 */
export interface AgvType {
  /** 種類の ID（AGV が使う種類を指す。加えるときに入れ、あとから変えない）。 */
  readonly id: string;
  /** 画面に出す名前（いつでも変えられる。ほかの種類と同じ名前は付けられない）。 */
  readonly name: string;
  /** 前進の速さ（mm/s）。 */
  readonly forward: number;
  /** 後退の速さ（mm/s）。 */
  readonly backward: number;
  /** 回転の速さ（°/s）。 */
  readonly rotation: number;
  /** 充電に向かう残量（%）。 */
  readonly chargeAt: number;
  /** 割り当てに戻す残量（%）。 */
  readonly resumeAt: number;
  /** 特殊消耗品受け渡しパネルを走れるか。 */
  readonly special: boolean;
}

/** 交通整理と配車の両方に登録してあるか（片方だけなら、起動したときの食い違い）。 */
export type Registration = 'both' | 'trafficOnly' | 'dispatchOnly';

/** 登録してある AGV。交通整理だけなら carrier と control がなく、配車だけなら type がない。type は種類の ID。 */
export interface AgvEntry {
  readonly id: AgvId;
  readonly type: string | null;
  readonly carrier: string | null;
  readonly control: ControlStatus | null;
  readonly registration: Registration;
}

export interface AgvDraft {
  readonly id: string;
  readonly type: string;
  readonly carrier: string;
}

/** AGV の種類の下書き（入力のままの文字）。 */
export interface TypeDraft {
  /** 種類の ID（加えるときだけ入れる）。 */
  readonly id: string;
  readonly name: string;
  readonly forward: string;
  readonly backward: string;
  readonly rotation: string;
  readonly chargeAt: string;
  readonly resumeAt: string;
  readonly special: boolean;
}

export interface ChargeDefaults {
  readonly chargeAt: number;
  readonly resumeAt: number;
}

/** existingNames は、ほかの種類の名前（変えるときは、自分の名前を含めない）。 */
export type TypeMode =
  | { readonly mode: 'add'; readonly existingIds: readonly string[]; readonly existingNames: readonly string[]; readonly defaults: ChargeDefaults }
  | { readonly mode: 'edit'; readonly existingNames: readonly string[]; readonly defaults: ChargeDefaults };

const CONFIGURE = 'AGV と AGV の種類の登録は、管理者のロールで使えます。';

export function configureBlockedReason(permissions: Permissions): string | null {
  return permissions.has('configure') ? null : CONFIGURE;
}

export const registrationLabel: Record<Registration, string> = {
  both: '登録済み',
  trafficOnly: '走行の登録だけ（配車に未登録）',
  dispatchOnly: '配車の登録だけ（走行に未登録）',
};

/** 並べ方：登録が食い違っているものを上にし、それぞれの中と残りは番号の順（一覧の決まり、docs/screens.md）。 */
export function sortAgvEntries(entries: readonly AgvEntry[]): AgvEntry[] {
  const odd = (a: AgvEntry) => Number(a.registration !== 'both');
  return [...entries].sort((a, b) => odd(b) - odd(a) || byIdNumber(a.id, b.id));
}

/** AGV の種類・キャリアの種類を変える、AGV を消すのを押せない理由。切り離しのときだけできる。 */
export function agvChangeBlockedReason(entry: AgvEntry, permissions: Permissions): string | null {
  const blocked = configureBlockedReason(permissions);
  if (blocked !== null) {
    return blocked;
  }
  if (entry.control !== null && entry.control !== 'detached') {
    return `切り離しのときだけ、変えたり消したりできます（いまは${controlStatusLabel[entry.control]}）。`;
  }
  return null;
}

/** AGV を保存できない理由（加えるときは番号も確かめる）。 */
export function agvProblem(draft: AgvDraft, existingIds: readonly string[] | null): string | null {
  if (existingIds !== null && draft.id.trim() === '') {
    return 'AGV の番号を入れてください。';
  }
  if (draft.type === '') {
    return 'AGV の種類を選んでください。';
  }
  if (draft.carrier === '') {
    return 'キャリアの種類を選んでください。';
  }
  if (existingIds?.includes(draft.id.trim())) {
    return `${draft.id.trim()} は、もう登録してあります。`;
  }
  return null;
}

const INTEGER = /^\d+$/;
const TYPE_ID = /^[A-Za-z0-9_-]+$/;

function speedOk(text: string): boolean {
  return INTEGER.test(text.trim()) && Number(text) >= 1;
}

function percentOk(text: string): boolean {
  return INTEGER.test(text.trim()) && Number(text) <= 100;
}

/** 種類を保存できない理由（保存できるなら null）。 */
export function typeProblem(draft: TypeDraft, mode: TypeMode): string | null {
  if (mode.mode === 'add') {
    const id = draft.id.trim();
    if (id === '') {
      return '種類の ID を入れてください。';
    }
    if (!TYPE_ID.test(id)) {
      return '種類の ID は、英数字と「-」「_」で入れてください。';
    }
    if (mode.existingIds.includes(id)) {
      return `ID ${id} の種類は、もう登録してあります。`;
    }
  }
  const name = draft.name.trim();
  if (name === '') {
    return '種類の名前を入れてください。';
  }
  if (mode.existingNames.includes(name)) {
    return `${name} という名前の種類は、もう登録してあります。`;
  }
  for (const [text, label] of [
    [draft.forward, '前進の速さ'],
    [draft.backward, '後退の速さ'],
    [draft.rotation, '回転の速さ'],
  ] as const) {
    if (!speedOk(text)) {
      return `${label}を、1 以上の整数で入れてください。`;
    }
  }
  for (const [text, label] of [
    [draft.chargeAt, '充電に向かう残量'],
    [draft.resumeAt, '割り当てに戻す残量'],
  ] as const) {
    if (!(mode.mode === 'add' && text.trim() === '') && !percentOk(text)) {
      return `${label}を、0〜100 の整数で入れてください。`;
    }
  }
  const t = toAgvType(draft, mode.defaults);
  if (t.resumeAt < t.chargeAt) {
    return `割り当てに戻す残量は、充電に向かう残量（${t.chargeAt}%）以上にしてください。`;
  }
  return null;
}

/** 保存する種類。空の充電の値は、既定値で埋める。 */
export function toAgvType(draft: TypeDraft, defaults: ChargeDefaults): AgvType {
  const percent = (text: string, fallback: number) => (text.trim() === '' ? fallback : Number(text));
  return {
    id: draft.id.trim(),
    name: draft.name.trim(),
    forward: Number(draft.forward),
    backward: Number(draft.backward),
    rotation: Number(draft.rotation),
    chargeAt: percent(draft.chargeAt, defaults.chargeAt),
    resumeAt: percent(draft.resumeAt, defaults.resumeAt),
    special: draft.special,
  };
}

export function typeDraftOf(type: AgvType): TypeDraft {
  return {
    id: type.id,
    name: type.name,
    forward: String(type.forward),
    backward: String(type.backward),
    rotation: String(type.rotation),
    chargeAt: String(type.chargeAt),
    resumeAt: String(type.resumeAt),
    special: type.special,
  };
}

/** 種類の ID から、画面に出す名前（知らない ID なら、ID のまま）。 */
export function typeNameOf(types: readonly AgvType[], id: string): string {
  return types.find((t) => t.id === id)?.name ?? id;
}

/** 種類を消すのを押せない理由。使う AGV がなければ消せる。 */
export function typeDeleteBlockedReason(type: AgvType, entries: readonly AgvEntry[], permissions: Permissions): string | null {
  const blocked = configureBlockedReason(permissions);
  if (blocked !== null) {
    return blocked;
  }
  const users = entries
    .filter((e) => e.type === type.id)
    .map((e) => e.id)
    .sort(byIdNumber);
  return users.length === 0 ? null : `${users.join('、')} が使っているので、消せません。`;
}
