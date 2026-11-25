import { parseVersion, type Versioned } from '../../../shared/realtime/versioned';

/** ストッカーのスロット（ストッカーの番号とスロットの番号。どちらも 1 から）。 */
export interface SlotPosition {
  readonly stocker: number;
  readonly slot: number;
}

/** 新品か中古品か（品種がわからないものは、記録の状態も確かでないので null）。 */
export type ServerCondition = 'new' | 'used' | null;

/** 回収が要るわけ。 */
export type RecoveryReason = 'mismatch' | 'notInSetting' | 'surplus' | 'discard';

/** 装置にある消耗品の、回収の様子。none：回収しない、needed：回収が要る（まだ頼んでいない）、requested：回収を頼んだ。 */
export type Recovery = { readonly state: 'none' } | { readonly state: 'needed'; readonly reason: RecoveryReason } | { readonly state: 'requested' };

/** 装置の中（加工部、スロット、受け渡し部）にある消耗品。 */
export interface HeldStatus {
  readonly id: string;
  /** 品種（わからないときは null）。 */
  readonly variety: string | null;
  readonly condition: ServerCondition;
  /** 照合のエラーや交換で、記録と現物が合わず、現物を確かめるまでスロットをふさいでいる。 */
  readonly mismatched: boolean;
  /** 装置から廃棄リクエストを受けた。 */
  readonly discardRequested: boolean;
  readonly recovery: Recovery;
}

/** スロットと、その中身（空いていれば null）。 */
export interface StockerSlotStatus extends SlotPosition {
  readonly consumable: HeldStatus | null;
}

/** サーバーから届く装置の様子（意味は docs/screens.md「装置とステーションの気をつけること」）。 */
export interface DeviceStatus {
  readonly id: string;
  /** 受け渡しパネル。 */
  readonly panel: string;
  /** 不足：届ける途中のものを引いた、まだ手当てのない数。 */
  readonly shortage: number;
  /** 回収待ち：回収が要ると判断してから、装置から取り出すまでの数。 */
  readonly collectWaiting: number;
  /** ID の食い違いが片付いていないスロット。 */
  readonly mismatchedSlots: readonly SlotPosition[];
  /** 段取り替えのエラーになった品種。 */
  readonly setupErrors: readonly string[];
  /** 品種ごとの、設定の個数とある新品。 */
  readonly stocker: readonly { readonly variety: string; readonly target: number; readonly have: number }[];
  readonly slots: { readonly total: number; readonly free: number; readonly delivering: number };
  /** 加工部にあるもの。 */
  readonly processing: readonly HeldStatus[];
  /** すべてのスロットの中身（番号の順）。取り込み中の元のスロットは、空きとして届く。 */
  readonly stockerSlots: readonly StockerSlotStatus[];
  /** 受け渡し部にあるもの。 */
  readonly handover: readonly HeldStatus[];
}

/** 要確認になったわけ。 */
export type CheckReason = 'unregisteredId' | 'foundAfterLost' | 'idMismatch';

/** ステーションにある消耗品の状態。 */
export type StationItemStatus =
  | { readonly kind: 'deliverable' }
  | { readonly kind: 'reserved'; readonly device: string }
  | { readonly kind: 'designated'; readonly device: string }
  | { readonly kind: 'needsCheck'; readonly reason: CheckReason }
  | { readonly kind: 'shipping' }
  | { readonly kind: 'notDeliverable' };

/** ステーションにある消耗品。 */
export interface StationItemData {
  readonly id: string;
  readonly variety: string | null;
  readonly condition: ServerCondition;
  /** ステーションに入った日時。 */
  readonly arrivedAt: Date;
  readonly state: StationItemStatus;
}

/** サーバーから届くステーションの様子。 */
export interface StationStatus {
  readonly id: string;
  /** 受け渡しパネル。 */
  readonly panel: string;
  /** 要確認の消耗品の数。 */
  readonly needsCheck: number;
  /** 補充を頼んで、まだ入っていない数（すべての品種の合計）。 */
  readonly replenishWaiting: number;
  /** 品種ごとの、届けられる新品と、届ける予約が付いている数と、補充待ちの数。 */
  readonly stock: readonly { readonly variety: string; readonly available: number; readonly reserved: number; readonly replenishWaiting: number }[];
  /** ステーションにある消耗品（ID の順）。 */
  readonly items: readonly StationItemData[];
}

/** サーバーから届く装置とステーションの様子。変わるたびに番号が進む。 */
export interface EquipmentStatusSnapshot extends Versioned {
  readonly devices: readonly DeviceStatus[];
  readonly stations: readonly StationStatus[];
  /** 登録してある品種（ID の順）。 */
  readonly varieties: readonly string[];
}

const isRecord = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null;

function count(value: unknown, what: string, min = 0): number {
  if (typeof value !== 'number' || !Number.isInteger(value) || value < min) {
    throw new TypeError(`${what}が正しくありません（${String(value)}）。`);
  }
  return value;
}

function text(value: unknown, what: string): string {
  if (typeof value !== 'string' || value.trim() === '') {
    throw new TypeError(`${what}がありません。`);
  }
  return value;
}

function optionalText(value: unknown, what: string): string | null {
  return value === null ? null : text(value, what);
}

function flag(value: unknown, what: string): boolean {
  if (typeof value !== 'boolean') {
    throw new TypeError(`${what}が正しくありません（${String(value)}）。`);
  }
  return value;
}

function records(value: unknown, what: string): readonly Record<string, unknown>[] {
  if (!Array.isArray(value) || !value.every(isRecord)) {
    throw new TypeError(`${what}の並びが正しくありません。`);
  }
  return value;
}

function texts(value: unknown, what: string): readonly string[] {
  if (!Array.isArray(value)) {
    throw new TypeError(`${what}の並びが正しくありません。`);
  }
  return value.map((v) => text(v, what));
}

function oneOf<T extends string>(value: unknown, choices: readonly T[], what: string): T {
  if (typeof value !== 'string' || !(choices as readonly string[]).includes(value)) {
    throw new TypeError(`${what}が正しくありません（${String(value)}）。`);
  }
  return value as T;
}

function condition(value: unknown, what: string): ServerCondition {
  return value === null ? null : oneOf(value, ['new', 'used'] as const, what);
}

function recovery(value: unknown, what: string): Recovery {
  if (!isRecord(value)) {
    throw new TypeError(`${what}の形が正しくありません。`);
  }
  const state = oneOf(value.state, ['none', 'needed', 'requested'] as const, what);
  switch (state) {
    case 'none':
      return { state };
    case 'needed':
      return { state, reason: oneOf(value.reason, ['mismatch', 'notInSetting', 'surplus', 'discard'] as const, `${what}のわけ`) };
    case 'requested':
      return { state };
    default:
      throw new Error(`知らない回収の様子です: ${JSON.stringify(state satisfies never)}`);
  }
}

function parseHeld(value: Record<string, unknown>, owner: string): HeldStatus {
  const id = text(value.id, `${owner} にある消耗品の ID`);
  return {
    id,
    variety: optionalText(value.variety, `${id} の品種`),
    condition: condition(value.condition, `${id} の新品・中古品`),
    mismatched: flag(value.mismatched, `${id} の食い違い`),
    discardRequested: flag(value.discardRequested, `${id} の廃棄リクエスト`),
    recovery: recovery(value.recovery, `${id} の回収の様子`),
  };
}

/** スロットの中身（空いていれば null）。 */
function slotConsumable(value: unknown, owner: string): HeldStatus | null {
  if (value === null) {
    return null;
  }
  if (!isRecord(value)) {
    throw new TypeError(`${owner} のスロットの中身の形が正しくありません。`);
  }
  return parseHeld(value, owner);
}

function parseDevice(value: Record<string, unknown>): DeviceStatus {
  const id = text(value.id, '装置の ID');
  if (!isRecord(value.slots)) {
    throw new TypeError(`装置 ${id} の様子の形が正しくありません。`);
  }
  return {
    id,
    panel: text(value.panel, `${id} の受け渡しパネル`),
    shortage: count(value.shortage, `${id} の不足`),
    collectWaiting: count(value.collectWaiting, `${id} の回収待ち`),
    mismatchedSlots: records(value.mismatchedSlots, `${id} の食い違ったスロット`).map((s) => ({
      stocker: count(s.stocker, `${id} のストッカーの番号`, 1),
      slot: count(s.slot, `${id} のスロットの番号`, 1),
    })),
    setupErrors: texts(value.setupErrors, `${id} の段取り替えのエラーの品種`),
    stocker: records(value.stocker, `${id} のストッカー`).map((l) => ({
      variety: text(l.variety, `${id} の品種`),
      target: count(l.target, `${id} の設定の個数`),
      have: count(l.have, `${id} のある新品`),
    })),
    slots: {
      total: count(value.slots.total, `${id} のスロットの数`),
      free: count(value.slots.free, `${id} の空きスロット`),
      delivering: count(value.slots.delivering, `${id} の届ける途中`),
    },
    processing: records(value.processing, `${id} の加工部`).map((h) => parseHeld(h, id)),
    stockerSlots: records(value.stockerSlots, `${id} のスロット`).map((s) => ({
      stocker: count(s.stocker, `${id} のストッカーの番号`, 1),
      slot: count(s.slot, `${id} のスロットの番号`, 1),
      consumable: slotConsumable(s.consumable, id),
    })),
    handover: records(value.handover, `${id} の受け渡し部`).map((h) => parseHeld(h, id)),
  };
}

function itemState(value: unknown, what: string): StationItemStatus {
  if (!isRecord(value)) {
    throw new TypeError(`${what}の形が正しくありません。`);
  }
  const kind = oneOf(value.kind, ['deliverable', 'reserved', 'designated', 'needsCheck', 'shipping', 'notDeliverable'] as const, what);
  switch (kind) {
    case 'reserved':
    case 'designated':
      return { kind, device: text(value.device, `${what}の装置`) };
    case 'needsCheck':
      return { kind, reason: oneOf(value.reason, ['unregisteredId', 'foundAfterLost', 'idMismatch'] as const, `${what}のわけ`) };
    case 'deliverable':
    case 'shipping':
    case 'notDeliverable':
      return { kind };
    default:
      throw new Error(`知らない状態です: ${JSON.stringify(kind satisfies never)}`);
  }
}

function dateOf(value: unknown, what: string): Date {
  const date = typeof value === 'string' ? new Date(value) : null;
  if (date === null || Number.isNaN(date.getTime())) {
    throw new TypeError(`${what}が正しくありません（${String(value)}）。`);
  }
  return date;
}

function parseStation(value: Record<string, unknown>): StationStatus {
  const id = text(value.id, 'ステーションの ID');
  return {
    id,
    panel: text(value.panel, `${id} の受け渡しパネル`),
    needsCheck: count(value.needsCheck, `${id} の要確認`),
    replenishWaiting: count(value.replenishWaiting, `${id} の補充待ち`),
    stock: records(value.stock, `${id} の在庫`).map((l) => ({
      variety: text(l.variety, `${id} の品種`),
      available: count(l.available, `${id} の届けられる数`),
      reserved: count(l.reserved, `${id} の届ける予約の数`),
      replenishWaiting: count(l.replenishWaiting, `${id} の補充待ち`),
    })),
    items: records(value.items, `${id} にある消耗品`).map((i) => {
      const item = text(i.id, `${id} にある消耗品の ID`);
      return {
        id: item,
        variety: optionalText(i.variety, `${item} の品種`),
        condition: condition(i.condition, `${item} の新品・中古品`),
        arrivedAt: dateOf(i.arrivedAt, `${item} の入庫した日時`),
        state: itemState(i.state, `${item} の状態`),
      };
    }),
  };
}

/** サーバーから届いた値を検証して、装置とステーションの様子にする。形が正しくなければ例外にする。 */
export function parseEquipmentStatus(value: unknown): EquipmentStatusSnapshot {
  if (!isRecord(value)) {
    throw new TypeError('装置とステーションの様子の形が正しくありません。');
  }
  return {
    version: parseVersion(value.version, '装置とステーションの様子'),
    devices: records(value.devices, '装置').map(parseDevice),
    stations: records(value.stations, 'ステーション').map(parseStation),
    varieties: texts(value.varieties, '品種'),
  };
}
