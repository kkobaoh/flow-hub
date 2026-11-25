import { panelId } from '../routing/model';
import type { CheckReason, DeviceStatus, HeldStatus, RecoveryReason, ServerCondition, StationItemStatus, StationStatus } from './api/equipmentStatus';
import type { DeviceInside, HeldConsumable, SettingRow, Slot, Stocker } from './devices';
import { equipmentId } from './equipment';
import type { Condition, StationInventory, StationItemState } from './station';

const conditionLabel = (condition: ServerCondition): Condition | null => (condition === 'new' ? '新品' : condition === 'used' ? '中古品' : null);

const recoveryLabel: Record<RecoveryReason, string> = {
  mismatch: 'ID の食い違い',
  notInSetting: '設定にない',
  surplus: '設定より多い',
  discard: '廃棄',
};

/** 装置の中にある消耗品を、画面に出す形にする。where は、加工部や受け渡し部にあるときに添える言葉。 */
function heldOf(h: HeldStatus, where: string | null = null): HeldConsumable {
  const variety = h.variety ?? '不明な消耗品';
  if (h.mismatched) {
    const recovery = h.recovery.state === 'requested' ? '回収を頼んだ' : '回収を待っている';
    return { id: h.id, variety, note: `ID が食い違った。${recovery}`, tone: 'blocked' };
  }
  const condition = conditionLabel(h.condition) ?? '状態は不明';
  switch (h.recovery.state) {
    case 'none':
      return { id: h.id, variety, note: where === null ? condition : `${condition}・${where}`, tone: 'normal' };
    case 'needed':
      return { id: h.id, variety, note: `${condition}・回収する（${recoveryLabel[h.recovery.reason]}）`, tone: 'collect' };
    case 'requested':
      return { id: h.id, variety, note: `${condition}・回収を頼んだ${h.discardRequested ? '（廃棄）' : ''}`, tone: 'collect' };
    default:
      throw new Error(`知らない回収の様子です: ${JSON.stringify(h.recovery satisfies never)}`);
  }
}

/** スロットを、ストッカーの番号ごとにまとめる。 */
function stockersOf(s: DeviceStatus): Stocker[] {
  const byStocker = new Map<number, Slot[]>();
  for (const slot of s.stockerSlots) {
    const slots = byStocker.get(slot.stocker) ?? [];
    slots.push({ number: slot.slot, content: slot.consumable === null ? { kind: 'empty' } : { kind: 'held', consumable: heldOf(slot.consumable) } });
    byStocker.set(slot.stocker, slots);
  }
  return [...byStocker.entries()]
    .sort(([a], [b]) => a - b)
    .map(([number, slots]) => ({ name: `ストッカー${number}`, layout: 'slots', slots: slots.sort((a, b) => a.number - b.number) }));
}

/** 品種ごとの状態：設定にない（設定の個数が 0）、足りない、設定より多い、足りている。 */
function settingStatus(target: number, have: number): SettingRow['status'] {
  if (target === 0) {
    return 'notInSetting';
  }
  return have < target ? 'short' : have > target ? 'excess' : 'enough';
}

/** サーバーから届いた装置の様子を、装置とストッカーの画面に出す形にする。 */
export function deviceInsideOf(s: DeviceStatus): DeviceInside {
  return {
    id: equipmentId(s.id),
    panel: panelId(s.panel),
    processing: s.processing.map((h) => heldOf(h, '加工部に取り込み中')),
    stockers: stockersOf(s),
    handover: s.handover.map((h) => heldOf(h, '受け渡し部にある')),
    setting: s.stocker.map((l) => ({ variety: l.variety, target: l.target, have: l.have, status: settingStatus(l.target, l.have) })),
  };
}

const checkReasonText: Record<CheckReason, string> = {
  unregisteredId: '登録されていない ID として入ってきました。',
  foundAfterLost: '所在不明だったものが、ステーションで見つかりました。',
  idMismatch: '装置で ID が食い違ったので、回収しました。',
};

function itemStateOf(state: StationItemStatus): StationItemState {
  switch (state.kind) {
    case 'deliverable':
      return { kind: 'available' };
    case 'reserved':
      return { kind: 'reserved', device: state.device };
    case 'designated':
      return { kind: 'nominated', device: state.device };
    case 'needsCheck':
      return { kind: 'needsCheck', reason: checkReasonText[state.reason] };
    case 'shipping':
      return { kind: 'shipping' };
    case 'notDeliverable':
      return { kind: 'notDeliverable' };
    default:
      throw new Error(`知らない状態です: ${JSON.stringify(state satisfies never)}`);
  }
}

/** サーバーから届いたステーションの様子を、ステーションの在庫の画面に出す形にする。 */
export function stationInventoryOf(s: StationStatus): StationInventory {
  return {
    id: equipmentId(s.id),
    panel: panelId(s.panel),
    summary: s.stock.map((l) => ({ variety: l.variety, available: l.available, reserved: l.reserved, replenishWaiting: l.replenishWaiting })),
    items: s.items.map((i) => ({ id: i.id, variety: i.variety, condition: conditionLabel(i.condition), receivedAt: i.arrivedAt, state: itemStateOf(i.state) })),
  };
}
