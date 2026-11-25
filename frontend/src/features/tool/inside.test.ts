import { describe, expect, it } from 'vitest';
import type { DeviceStatus, HeldStatus, StationStatus } from './api/equipmentStatus';
import { deviceInsideOf, stationInventoryOf } from './inside';

const held = (more: Partial<HeldStatus> = {}): HeldStatus => ({
  id: 'T-1',
  variety: 'DR-5',
  condition: 'new',
  mismatched: false,
  discardRequested: false,
  recovery: { state: 'none' },
  ...more,
});

const device = (more: Partial<DeviceStatus> = {}): DeviceStatus => ({
  id: 'DEV-1',
  panel: 'D1',
  shortage: 0,
  collectWaiting: 0,
  mismatchedSlots: [],
  setupErrors: [],
  stocker: [],
  slots: { total: 0, free: 0, delivering: 0 },
  processing: [],
  stockerSlots: [],
  handover: [],
  ...more,
});

describe('deviceInsideOf（装置とストッカーの画面に出す、装置の中）', () => {
  it('スロットはストッカーごとにまとめ、ある消耗品か空きを出す', () => {
    const inside = deviceInsideOf(
      device({
        stockerSlots: [
          { stocker: 1, slot: 1, consumable: held() },
          { stocker: 1, slot: 2, consumable: null },
          { stocker: 2, slot: 1, consumable: held({ id: 'T-2', condition: 'used' }) },
        ],
      }),
    );

    expect(inside).toMatchObject({ id: 'DEV-1', panel: 'D1' });
    expect(inside.stockers).toEqual([
      {
        name: 'ストッカー1',
        layout: 'slots',
        slots: [
          { number: 1, content: { kind: 'held', consumable: { id: 'T-1', variety: 'DR-5', note: '新品', tone: 'normal' } } },
          { number: 2, content: { kind: 'empty' } },
        ],
      },
      { name: 'ストッカー2', layout: 'slots', slots: [{ number: 1, content: { kind: 'held', consumable: { id: 'T-2', variety: 'DR-5', note: '中古品', tone: 'normal' } } }] },
    ]);
  });

  it.each([
    [held({ recovery: { state: 'needed', reason: 'notInSetting' } }), '新品・回収する（設定にない）', 'collect'],
    [held({ recovery: { state: 'needed', reason: 'surplus' } }), '新品・回収する（設定より多い）', 'collect'],
    [held({ condition: 'used', discardRequested: true, recovery: { state: 'needed', reason: 'discard' } }), '中古品・回収する（廃棄）', 'collect'],
    [held({ condition: 'used', discardRequested: true, recovery: { state: 'requested' } }), '中古品・回収を頼んだ（廃棄）', 'collect'],
    [held({ recovery: { state: 'requested' } }), '新品・回収を頼んだ', 'collect'],
    [held({ variety: null, condition: null, mismatched: true, recovery: { state: 'needed', reason: 'mismatch' } }), 'ID が食い違った。回収を待っている', 'blocked'],
    [held({ variety: null, condition: null, mismatched: true, recovery: { state: 'requested' } }), 'ID が食い違った。回収を頼んだ', 'blocked'],
  ] as const)('回収するものと食い違ったものは、わけを書いて目立たせる（%#）', (h, note, tone) => {
    const [stocker] = deviceInsideOf(device({ stockerSlots: [{ stocker: 1, slot: 1, consumable: h }] })).stockers;

    expect(stocker?.layout === 'slots' && stocker.slots[0]?.content).toMatchObject({ kind: 'held', consumable: { note, tone } });
  });

  it('品種がわからないものは「不明な消耗品」と出す', () => {
    const [stocker] = deviceInsideOf(device({ stockerSlots: [{ stocker: 1, slot: 1, consumable: held({ variety: null, condition: null }) }] })).stockers;

    expect(stocker?.layout === 'slots' && stocker.slots[0]?.content).toMatchObject({ consumable: { variety: '不明な消耗品', note: '状態は不明' } });
  });

  it('加工部と受け渡し部にあるものも出す', () => {
    const inside = deviceInsideOf(device({ processing: [held({ condition: 'used' })], handover: [held({ id: 'T-3' })] }));

    expect(inside.processing).toEqual([{ id: 'T-1', variety: 'DR-5', note: '中古品・加工部に取り込み中', tone: 'normal' }]);
    expect(inside.handover).toEqual([{ id: 'T-3', variety: 'DR-5', note: '新品・受け渡し部にある', tone: 'normal' }]);
  });

  it('品種ごとの状態は、設定の個数とある新品から決める', () => {
    const inside = deviceInsideOf(
      device({
        stocker: [
          { variety: 'A', target: 2, have: 2 },
          { variety: 'B', target: 2, have: 1 },
          { variety: 'C', target: 1, have: 2 },
          { variety: 'D', target: 0, have: 1 },
        ],
      }),
    );

    expect(inside.setting.map((r) => r.status)).toEqual(['enough', 'short', 'excess', 'notInSetting']);
  });
});

const station = (more: Partial<StationStatus> = {}): StationStatus => ({
  id: 'ST-1',
  panel: 'ST',
  needsCheck: 0,
  replenishWaiting: 0,
  stock: [],
  items: [],
  ...more,
});

describe('stationInventoryOf（ステーションの在庫の画面に出す、ステーション）', () => {
  const at = new Date('2026-11-25T09:30:00Z');

  it('品種ごとの数と、消耗品ごとの状態を出す', () => {
    const inventory = stationInventoryOf(
      station({
        stock: [{ variety: 'DR-5', available: 1, reserved: 1, replenishWaiting: 2 }],
        items: [
          { id: 'T-1', variety: 'DR-5', condition: 'new', arrivedAt: at, state: { kind: 'deliverable' } },
          { id: 'T-2', variety: 'DR-5', condition: 'new', arrivedAt: at, state: { kind: 'reserved', device: 'DEV-1' } },
          { id: 'T-3', variety: 'DR-5', condition: 'new', arrivedAt: at, state: { kind: 'designated', device: 'DEV-2' } },
          { id: 'T-4', variety: 'DR-5', condition: 'new', arrivedAt: at, state: { kind: 'shipping' } },
          { id: 'U-1', variety: 'DR-5', condition: 'used', arrivedAt: at, state: { kind: 'notDeliverable' } },
        ],
      }),
    );

    expect(inventory).toMatchObject({ id: 'ST-1', panel: 'ST', summary: [{ variety: 'DR-5', available: 1, reserved: 1, replenishWaiting: 2 }] });
    expect(inventory.items).toEqual([
      { id: 'T-1', variety: 'DR-5', condition: '新品', receivedAt: at, state: { kind: 'available' } },
      { id: 'T-2', variety: 'DR-5', condition: '新品', receivedAt: at, state: { kind: 'reserved', device: 'DEV-1' } },
      { id: 'T-3', variety: 'DR-5', condition: '新品', receivedAt: at, state: { kind: 'nominated', device: 'DEV-2' } },
      { id: 'T-4', variety: 'DR-5', condition: '新品', receivedAt: at, state: { kind: 'shipping' } },
      { id: 'U-1', variety: 'DR-5', condition: '中古品', receivedAt: at, state: { kind: 'notDeliverable' } },
    ]);
  });

  it.each([
    ['unregisteredId', '登録されていない ID として入ってきました。'],
    ['foundAfterLost', '所在不明だったものが、ステーションで見つかりました。'],
    ['idMismatch', '装置で ID が食い違ったので、回収しました。'],
  ] as const)('要確認には、要確認になったわけを付ける（%s）', (reason, text) => {
    const inventory = stationInventoryOf(station({ items: [{ id: 'X-1', variety: null, condition: null, arrivedAt: at, state: { kind: 'needsCheck', reason } }] }));

    expect(inventory.items[0]).toEqual({ id: 'X-1', variety: null, condition: null, receivedAt: at, state: { kind: 'needsCheck', reason: text } });
  });
});
