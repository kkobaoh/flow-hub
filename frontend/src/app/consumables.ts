import type { DeviceInside } from './devices';
import { byIdNumber, matchesQuery } from './listing';
import type { Permissions } from './permissions';
import type { Condition } from './station';

/** 消耗品の所在。 */
export type ConsumableLocation =
  | { readonly kind: 'station'; readonly station: string }
  | { readonly kind: 'slot'; readonly device: string; readonly stocker: number; readonly slot: number }
  /** プレートに載せたまま置くストッカー。 */
  | { readonly kind: 'plate'; readonly device: string; readonly stocker: number }
  | { readonly kind: 'processing'; readonly device: string }
  | { readonly kind: 'handover'; readonly device: string }
  | { readonly kind: 'agv'; readonly agv: string }
  | { readonly kind: 'unknown' };

export interface HistoryEntry {
  readonly at: Date;
  readonly what: string;
  readonly detail: string;
}

/** 1個の消耗品の記録。 */
export interface ConsumableRecord {
  readonly id: string;
  readonly variety: string | null;
  readonly processKind: string | null;
  readonly condition: Condition | null;
  readonly location: ConsumableLocation;
  readonly needsCheck: boolean;
  /** 届ける・回収するの予約。assigned は、その搬送に AGV を割り当て済みか。 */
  readonly reservation: { readonly transport: number; readonly assigned: boolean; readonly purpose: string } | null;
  readonly nomination: { readonly device: string; readonly assigned: boolean } | null;
  readonly shipping: boolean;
  /** これまでの動き（新しい順）。 */
  readonly history: readonly HistoryEntry[];
}

/** 登録・直すときの下書き。location は所在のキー（locationKey）、未選択は ''。 */
export interface RecordDraft {
  readonly id: string;
  readonly variety: string;
  readonly condition: Condition | null;
  readonly location: string;
}

export function locationLabel(location: ConsumableLocation): string {
  switch (location.kind) {
    case 'station':
      return location.station;
    case 'slot':
      return `${location.device} のストッカー${location.stocker}・スロット${location.slot}`;
    case 'plate':
      return `${location.device} のストッカー${location.stocker}`;
    case 'processing':
      return `${location.device} の加工部`;
    case 'handover':
      return `${location.device} の受け渡し部`;
    case 'agv':
      return `${location.agv} の中`;
    case 'unknown':
      return '所在不明';
  }
}

/** 所在を、選ぶ欄の値にするキー。同じ所在なら同じキーになる。 */
export function locationKey(location: ConsumableLocation): string {
  return JSON.stringify(location);
}

/** ID で探す：一部でも合うものを、ID の順に並べる。何も入れていなければ、何も出さない。 */
export function findConsumables(records: readonly ConsumableRecord[], query: string): ConsumableRecord[] {
  if (query.trim() === '') {
    return [];
  }
  return records.filter((r) => matchesQuery(r.id, query.trim())).sort((a, b) => byIdNumber(a.id, b.id));
}

/** 記録を直す・消すのを押せない理由（押せるなら null）。 */
export function editBlockedReason(record: ConsumableRecord, permissions: Permissions): string | null {
  if (!permissions.has('stockEdit')) {
    return '記録を直す・消すのは、保守以上のロールで使えます。';
  }
  switch (record.location.kind) {
    case 'agv':
      return 'AGV に積んでいる消耗品は、直したり消したりできません。';
    case 'processing':
      return '加工部にある消耗品は、直したり消したりできません。';
    case 'handover':
      return '受け渡し部にある消耗品は、直したり消したりできません。';
    default:
      break;
  }
  if (record.reservation?.assigned) {
    return `搬送 #${record.reservation.transport} に AGV を割り当て済みなので、直したり消したりできません。`;
  }
  if (record.shipping) {
    return '出庫を頼んでいる消耗品は、直したり消したりできません。';
  }
  return null;
}

const NOMINATE_PERMISSION = '指名と AGV での交換は、運用以上のロールで使えます。';

/** 指名を押せない理由（押せるなら null）。ステーションにある、要確認でない新品で、予約も出庫の頼みもないものだけ。 */
export function nominateBlockedReason(record: ConsumableRecord, permissions: Permissions): string | null {
  if (!permissions.has('consumableOps')) {
    return NOMINATE_PERMISSION;
  }
  const can = record.location.kind === 'station' && !record.needsCheck && record.condition === '新品' && record.reservation === null && !record.shipping;
  return can ? null : '指名できるのは、ステーションにある、要確認でない新品で、届ける予約も出庫の頼みもないものです。';
}

/** 指名の取り消しを押せない理由（押せるなら null）。割り当て前なら取り消せる。 */
export function cancelNominationBlockedReason(record: ConsumableRecord, permissions: Permissions): string | null {
  if (!permissions.has('consumableOps')) {
    return NOMINATE_PERMISSION;
  }
  return record.nomination?.assigned ? '届ける搬送に AGV を割り当て済みなので、指名を取り消せません。' : null;
}

/** 登録・直すときに選べる所在：ステーションと、装置のストッカーの空きスロット（プレートはストッカー）。直すときは、いまの所在も。 */
export function locationOptions(stations: readonly string[], devices: readonly DeviceInside[], current: ConsumableLocation | null): ConsumableLocation[] {
  const options: ConsumableLocation[] = [...stations].sort(byIdNumber).map((station) => ({ kind: 'station', station }));
  for (const d of [...devices].sort((a, b) => byIdNumber(a.id, b.id))) {
    d.stockers.forEach((s, i) => {
      if (s.layout === 'plate') {
        options.push({ kind: 'plate', device: d.id, stocker: i + 1 });
        return;
      }
      for (const slot of s.slots) {
        if (slot.content.kind === 'empty') {
          options.push({ kind: 'slot', device: d.id, stocker: i + 1, slot: slot.number });
        }
      }
    });
  }
  if (current !== null && current.kind !== 'unknown' && !options.some((o) => locationKey(o) === locationKey(current))) {
    options.push(current);
  }
  return options;
}

/** 直すときの下書き：いまの記録を入れ、記録にない項目（品種が不明、所在不明など）は未選択。 */
export function draftOfRecord(record: ConsumableRecord): RecordDraft {
  return {
    id: record.id,
    variety: record.variety ?? '',
    condition: record.condition,
    location: record.location.kind === 'unknown' ? '' : locationKey(record.location),
  };
}

export type RecordMode = { readonly mode: 'register'; readonly existingIds: readonly string[] } | { readonly mode: 'edit'; readonly original: RecordDraft };

/** 保存できない理由（保存できるなら null）。すべて入れるまで保存できない。直すときは、何か変えるまで保存できない。 */
export function recordProblem(draft: RecordDraft, mode: RecordMode): string | null {
  if (draft.id.trim() === '') {
    return 'ID を入れてください。';
  }
  if (draft.variety === '') {
    return '品種を選んでください。';
  }
  if (draft.condition === null) {
    return '新品か中古品かを選んでください。';
  }
  if (draft.location === '') {
    return '所在を選んでください。';
  }
  if (mode.mode === 'register' && mode.existingIds.includes(draft.id.trim())) {
    return `${draft.id.trim()} は、もう登録してあります。`;
  }
  if (mode.mode === 'edit' && draft.variety === mode.original.variety && draft.condition === mode.original.condition && draft.location === mode.original.location) {
    return '変えたところがありません。';
  }
  return null;
}
