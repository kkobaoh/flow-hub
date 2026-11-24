import { parseVersion, type Versioned } from '../../../shared/realtime/versioned';

/** ストッカーのスロット（ストッカーの番号とスロットの番号。どちらも 1 から）。 */
export interface SlotPosition {
  readonly stocker: number;
  readonly slot: number;
}

/** サーバーから届く装置の様子（意味は docs/screens.md「装置とステーションの気をつけること」）。 */
export interface DeviceStatus {
  readonly id: string;
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
}

/** サーバーから届くステーションの様子。 */
export interface StationStatus {
  readonly id: string;
  /** 要確認の消耗品の数。 */
  readonly needsCheck: number;
  /** 補充を頼んで、まだ入っていない数（すべての品種の合計）。 */
  readonly replenishWaiting: number;
  /** 品種ごとの、届けられる新品と補充待ちの数。 */
  readonly stock: readonly { readonly variety: string; readonly available: number; readonly replenishWaiting: number }[];
}

/** サーバーから届く装置とステーションの様子。変わるたびに番号が進む。 */
export interface EquipmentStatusSnapshot extends Versioned {
  readonly devices: readonly DeviceStatus[];
  readonly stations: readonly StationStatus[];
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

function records(value: unknown, what: string): readonly Record<string, unknown>[] {
  if (!Array.isArray(value) || !value.every(isRecord)) {
    throw new TypeError(`${what}の並びが正しくありません。`);
  }
  return value;
}

function parseDevice(value: Record<string, unknown>): DeviceStatus {
  const id = text(value.id, '装置の ID');
  if (!isRecord(value.slots) || !Array.isArray(value.setupErrors)) {
    throw new TypeError(`装置 ${id} の様子の形が正しくありません。`);
  }
  return {
    id,
    shortage: count(value.shortage, `${id} の不足`),
    collectWaiting: count(value.collectWaiting, `${id} の回収待ち`),
    mismatchedSlots: records(value.mismatchedSlots, `${id} の食い違ったスロット`).map((s) => ({
      stocker: count(s.stocker, `${id} のストッカーの番号`, 1),
      slot: count(s.slot, `${id} のスロットの番号`, 1),
    })),
    setupErrors: value.setupErrors.map((v) => text(v, `${id} の段取り替えのエラーの品種`)),
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
  };
}

function parseStation(value: Record<string, unknown>): StationStatus {
  const id = text(value.id, 'ステーションの ID');
  return {
    id,
    needsCheck: count(value.needsCheck, `${id} の要確認`),
    replenishWaiting: count(value.replenishWaiting, `${id} の補充待ち`),
    stock: records(value.stock, `${id} の在庫`).map((l) => ({
      variety: text(l.variety, `${id} の品種`),
      available: count(l.available, `${id} の届けられる数`),
      replenishWaiting: count(l.replenishWaiting, `${id} の補充待ち`),
    })),
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
  };
}
