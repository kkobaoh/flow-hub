import { agvId } from '../../fleet/agv';
import type { ConsumableHit } from '../../routing/ListPanel';
import { parseVersion, type Versioned } from '../../../shared/realtime/versioned';
import { locationLabel, type ConsumableLocation, type ConsumableRecord } from '../consumables';
import { equipmentId } from '../equipment';
import type { CheckReason, ServerCondition } from './equipmentStatus';

/** 届ける・回収するの予約（サーバーの ReservationView）。 */
export interface ReservationData {
  readonly kind: 'delivery' | 'recovery';
  /** 届け先の装置（届けるときだけ）。 */
  readonly device: string | null;
  /** 入っている搬送の番号（わからなければ null）。 */
  readonly transport: number | null;
  /** その搬送に AGV を割り当て済みか。 */
  readonly assigned: boolean;
}

/** サーバーから届く、1個の消耗品の記録。 */
export interface ConsumableData {
  readonly id: string;
  readonly variety: string | null;
  readonly processType: string | null;
  readonly condition: ServerCondition;
  readonly location: ConsumableLocation;
  /** 要確認のわけ。要確認でなければ null。 */
  readonly checkReason: CheckReason | null;
  readonly reservation: ReservationData | null;
  readonly designation: { readonly device: string; readonly assigned: boolean } | null;
  readonly shipping: boolean;
}

/** サーバーから届く、すべての消耗品の記録。変わるたびに番号が進む。 */
export interface ConsumablesSnapshot extends Versioned {
  readonly consumables: readonly ConsumableData[];
}

const isRecord = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null;

function text(value: unknown, what: string): string {
  if (typeof value !== 'string' || value.trim() === '') {
    throw new TypeError(`${what}がありません。`);
  }
  return value;
}

const optionalText = (value: unknown, what: string): string | null => (value === null ? null : text(value, what));

function number(value: unknown, what: string): number {
  if (typeof value !== 'number' || !Number.isInteger(value) || value < 1) {
    throw new TypeError(`${what}が正しくありません（${String(value)}）。`);
  }
  return value;
}

function flag(value: unknown, what: string): boolean {
  if (typeof value !== 'boolean') {
    throw new TypeError(`${what}が正しくありません（${String(value)}）。`);
  }
  return value;
}

function oneOf<T extends string>(value: unknown, choices: readonly T[], what: string): T {
  if (typeof value !== 'string' || !(choices as readonly string[]).includes(value)) {
    throw new TypeError(`${what}が正しくありません（${String(value)}）。`);
  }
  return value as T;
}

function location(value: unknown, what: string): ConsumableLocation {
  if (!isRecord(value)) {
    throw new TypeError(`${what}の形が正しくありません。`);
  }
  const kind = oneOf(value.kind, ['station', 'slot', 'processing', 'handover', 'agv', 'unknown'] as const, what);
  switch (kind) {
    case 'station':
      return { kind, station: text(value.station, `${what}のステーション`) };
    case 'slot':
      return { kind, device: text(value.device, `${what}の装置`), stocker: number(value.stocker, `${what}のストッカー`), slot: number(value.slot, `${what}のスロット`) };
    case 'processing':
    case 'handover':
      return { kind, device: text(value.device, `${what}の装置`) };
    case 'agv':
      return { kind, agv: optionalText(value.agv, `${what}の AGV`) };
    case 'unknown':
      return { kind };
    default:
      throw new Error(`知らない所在です: ${JSON.stringify(kind satisfies never)}`);
  }
}

function parseConsumable(value: unknown): ConsumableData {
  if (!isRecord(value)) {
    throw new TypeError('消耗品の形が正しくありません。');
  }
  const id = text(value.id, '消耗品の ID');
  const r = value.reservation;
  const d = value.designation;
  if ((r !== null && !isRecord(r)) || (d !== null && !isRecord(d))) {
    throw new TypeError(`${id} の予約か指名の形が正しくありません。`);
  }
  return {
    id,
    variety: optionalText(value.variety, `${id} の品種`),
    processType: optionalText(value.processType, `${id} の加工の種類`),
    condition: value.condition === null ? null : oneOf(value.condition, ['new', 'used'] as const, `${id} の新品・中古品`),
    location: location(value.location, `${id} の所在`),
    checkReason: value.checkReason === null ? null : oneOf(value.checkReason, ['unregisteredId', 'foundAfterLost', 'idMismatch'] as const, `${id} の要確認のわけ`),
    reservation:
      r === null
        ? null
        : {
            kind: oneOf(r.kind, ['delivery', 'recovery'] as const, `${id} の予約`),
            device: optionalText(r.device, `${id} の予約の装置`),
            transport: r.transport === null ? null : number(r.transport, `${id} の予約の搬送の番号`),
            assigned: flag(r.assigned, `${id} の予約の割り当て`),
          },
    designation: d === null ? null : { device: text(d.device, `${id} の指名の装置`), assigned: flag(d.assigned, `${id} の指名の割り当て`) },
    shipping: flag(value.shipping, `${id} の出庫の頼み`),
  };
}

/** サーバーから届いた値を検証して、すべての消耗品の記録にする。形が正しくなければ例外にする。 */
export function parseConsumables(value: unknown): ConsumablesSnapshot {
  if (!isRecord(value) || !Array.isArray(value.consumables)) {
    throw new TypeError('消耗品の記録の形が正しくありません。');
  }
  return { version: parseVersion(value.version, '消耗品の記録'), consumables: value.consumables.map(parseConsumable) };
}

/** 予約の目的の文（届ける先か、回収すること）。 */
const purposeOf = (r: ReservationData): string => (r.kind === 'delivery' ? `${r.device ?? '装置'} へ届ける` : '装置から回収する');

/** サーバーから届いた記録を、消耗品の検索の画面に出す形にする。 */
export function toRecord(d: ConsumableData): ConsumableRecord {
  return {
    id: d.id,
    variety: d.variety,
    processKind: d.processType,
    condition: d.condition === 'new' ? '新品' : d.condition === 'used' ? '中古品' : null,
    location: d.location,
    needsCheck: d.checkReason !== null,
    reservation: d.reservation === null ? null : { transport: d.reservation.transport, assigned: d.reservation.assigned, purpose: purposeOf(d.reservation) },
    nomination: d.designation,
    shipping: d.shipping,
  };
}

/** マップの「消耗品を探す」に出す状態の文（要確認、出庫を頼んだ、予約、指名の順に1つ）。 */
function statusOf(d: ConsumableData): string {
  if (d.checkReason !== null) {
    return '要確認';
  }
  if (d.shipping) {
    return '出庫を頼んだ';
  }
  if (d.reservation !== null) {
    const label = d.reservation.kind === 'delivery' ? '届ける予約あり' : '回収の予約あり';
    const number = d.reservation.transport === null ? '' : `搬送 #${d.reservation.transport}、`;
    return `${label}（${number}${purposeOf(d.reservation)}）`;
  }
  if (d.designation !== null) {
    return `指名あり（${d.designation.device}）`;
  }
  return '予約なし';
}

/** マップで目立たせる場所（AGV か、装置かステーション）。所在不明や、積んでいる AGV がわからなければ null。 */
function placeOf(l: ConsumableLocation): ConsumableHit['at'] {
  switch (l.kind) {
    case 'station':
      return { kind: 'equipment', id: equipmentId(l.station) };
    case 'slot':
    case 'plate':
    case 'processing':
    case 'handover':
      return { kind: 'equipment', id: equipmentId(l.device) };
    case 'agv':
      return l.agv === null ? null : { kind: 'agv', id: agvId(l.agv) };
    case 'unknown':
      return null;
    default:
      throw new Error(`知らない所在です: ${JSON.stringify(l satisfies never)}`);
  }
}

/** サーバーから届いた記録を、マップの「消耗品を探す」に出す形にする。 */
export function toHit(d: ConsumableData): ConsumableHit {
  const record = toRecord(d);
  return {
    id: d.id,
    variety: d.variety ?? '不明',
    condition: record.condition ?? '不明',
    where: locationLabel(d.location),
    status: statusOf(d),
    at: placeOf(d.location),
  };
}
