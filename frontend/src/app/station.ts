import type { EquipmentId } from '../map/equipment';
import type { PanelId } from '../map/model';
import { byIdNumber } from './listing';
import type { Permissions } from './permissions';
import { clockOf } from './transports';

export type Condition = '新品' | '中古品';

/** ステーションにある消耗品の状態。 */
export type StationItemState =
  | { readonly kind: 'available' }
  /** 届ける予約が付いている。assigned は、その搬送に AGV を割り当て済みか。 */
  | { readonly kind: 'reserved'; readonly transport: number; readonly assigned: boolean }
  | { readonly kind: 'nominated'; readonly device: string }
  /** 要確認。reason は、要確認になったわけ（サーバーが知らせる文）。 */
  | { readonly kind: 'needsCheck'; readonly reason: string }
  | { readonly kind: 'shipping' };

export interface StationItem {
  readonly id: string;
  /** 品種（わからないときは null）。 */
  readonly variety: string | null;
  readonly condition: Condition | null;
  readonly receivedAt: Date;
  readonly state: StationItemState;
}

/** 品種ごとの数。 */
export interface StockSummary {
  readonly variety: string;
  /** 届けられる新品。 */
  readonly available: number;
  /** 届ける予約が付いている数。 */
  readonly reserved: number;
  /** 補充を頼んで、まだ入っていない数。 */
  readonly replenishWaiting: number;
}

export interface StationInventory {
  readonly id: EquipmentId;
  readonly panel: PanelId;
  readonly summary: readonly StockSummary[];
  readonly items: readonly StationItem[];
}

/** 直した内容。 */
export interface Fix {
  readonly variety: string;
  readonly condition: Condition | null;
}

/** 並べ方：要確認を上にし、それぞれの中と残りは ID の順（docs/screens.md）。 */
export function sortStationItems(items: readonly StationItem[]): StationItem[] {
  const check = (i: StationItem) => Number(i.state.kind === 'needsCheck');
  return [...items].sort((a, b) => check(b) - check(a) || byIdNumber(a.id, b.id));
}

export function stateLabel(state: StationItemState): string {
  switch (state.kind) {
    case 'available':
      return '届けられる';
    case 'reserved':
      return `届ける予約あり（搬送 #${state.transport}）`;
    case 'nominated':
      return `指名あり（${state.device}）`;
    case 'needsCheck':
      return '要確認';
    case 'shipping':
      return '出庫を頼んだ';
  }
}

const NO_PERMISSION = '出庫と、要確認の消耗品を直すのは、運用以上のロールで使えます。';

/** 出庫を押せない理由（押せるなら null）。届ける搬送に AGV を割り当て済みのものと、出庫を頼んだものは出せない。 */
export function shipBlockedReason(item: StationItem, permissions: Permissions): string | null {
  if (!permissions.has('consumableOps')) {
    return NO_PERMISSION;
  }
  if (item.state.kind === 'reserved' && item.state.assigned) {
    return `届ける搬送 #${item.state.transport} に AGV を割り当て済みなので、出庫できません。`;
  }
  if (item.state.kind === 'shipping') {
    return '出庫を頼んでいます。';
  }
  return null;
}

/** 要確認の消耗品を直すのを、押せない理由（押せるなら null）。 */
export function fixBlockedReason(permissions: Permissions): string | null {
  return permissions.has('consumableOps') ? null : NO_PERMISSION;
}

/** 出庫の確かめる画面に出す文。 */
export function shipNote(item: StationItem): string {
  const base = 'ステーションに取り出しを頼みます。出たら、記録から消します。';
  switch (item.state.kind) {
    case 'reserved':
      return `届ける予約（搬送 #${item.state.transport}）を解いて出します。${base}`;
    case 'nominated':
      return `指名（${item.state.device}）を外して出します。${base}`;
    default:
      return base;
  }
}

/** 直すのを保存できない理由（保存できるなら null）。品種と、新品か中古品かを、どちらも選ぶ。 */
export function fixProblem(fix: Fix): string | null {
  if (fix.variety === '' && fix.condition === null) {
    return '品種と、新品か中古品かを選んでください。';
  }
  if (fix.variety === '') {
    return '品種を選んでください。';
  }
  if (fix.condition === null) {
    return '新品か中古品かを選んでください。';
  }
  return null;
}

/** 日時を「11/20 14:02」の形にする。 */
export function dateTimeOf(time: Date): string {
  return `${time.getMonth() + 1}/${time.getDate()} ${clockOf(time)}`;
}
