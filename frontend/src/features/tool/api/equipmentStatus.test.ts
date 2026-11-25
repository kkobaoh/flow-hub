import { describe, expect, it } from 'vitest';
import { parseEquipmentStatus } from './equipmentStatus';

const held = { id: 'T-1', variety: 'DR-5', condition: 'new', mismatched: false, discardRequested: false, recovery: { state: 'none', reason: null } };
const device = {
  id: 'DEV-1',
  panel: 'D1',
  shortage: 2,
  collectWaiting: 1,
  mismatchedSlots: [{ stocker: 1, slot: 3 }],
  setupErrors: ['DR-8'],
  stocker: [{ variety: 'DR-5', target: 2, have: 1 }],
  slots: { total: 10, free: 7, delivering: 1 },
  processing: [{ ...held, id: 'T-0', condition: 'used' }],
  stockerSlots: [
    { stocker: 1, slot: 1, consumable: held },
    { stocker: 1, slot: 2, consumable: null },
    { stocker: 1, slot: 3, consumable: { ...held, id: 'X-9', variety: null, condition: null, mismatched: true, recovery: { state: 'needed', reason: 'mismatch' } } },
  ],
  handover: [],
};
const station = {
  id: 'ST-1',
  panel: 'ST',
  needsCheck: 1,
  replenishWaiting: 2,
  stock: [{ variety: 'DR-5', available: 3, reserved: 1, replenishWaiting: 2 }],
  items: [
    { id: 'T-3', variety: 'DR-5', condition: 'new', arrivedAt: '2026-11-25T09:30:00+00:00', state: { kind: 'reserved', device: 'DEV-1', reason: null } },
    { id: 'X-1', variety: null, condition: null, arrivedAt: '2026-11-25T08:12:00+00:00', state: { kind: 'needsCheck', device: null, reason: 'unregisteredId' } },
  ],
};
const snapshot = { version: 4, devices: [device], stations: [station], varieties: ['DR-5', 'DR-6'] };

describe('parseEquipmentStatus', () => {
  it('サーバーから届いた装置とステーションの様子を、型にする', () => {
    const parsed = parseEquipmentStatus(snapshot);

    expect(parsed.version).toBe(4);
    expect(parsed.varieties).toEqual(['DR-5', 'DR-6']);
    const [dev] = parsed.devices;
    expect(dev).toMatchObject({ id: 'DEV-1', panel: 'D1', shortage: 2, stocker: device.stocker, slots: device.slots });
    expect(dev?.processing).toEqual([{ id: 'T-0', variety: 'DR-5', condition: 'used', mismatched: false, discardRequested: false, recovery: { state: 'none' } }]);
    expect(dev?.stockerSlots).toEqual([
      { stocker: 1, slot: 1, consumable: { id: 'T-1', variety: 'DR-5', condition: 'new', mismatched: false, discardRequested: false, recovery: { state: 'none' } } },
      { stocker: 1, slot: 2, consumable: null },
      { stocker: 1, slot: 3, consumable: { id: 'X-9', variety: null, condition: null, mismatched: true, discardRequested: false, recovery: { state: 'needed', reason: 'mismatch' } } },
    ]);
    const [st] = parsed.stations;
    expect(st).toMatchObject({ id: 'ST-1', panel: 'ST', needsCheck: 1, stock: station.stock });
    expect(st?.items).toEqual([
      { id: 'T-3', variety: 'DR-5', condition: 'new', arrivedAt: new Date('2026-11-25T09:30:00Z'), state: { kind: 'reserved', device: 'DEV-1' } },
      { id: 'X-1', variety: null, condition: null, arrivedAt: new Date('2026-11-25T08:12:00Z'), state: { kind: 'needsCheck', reason: 'unregisteredId' } },
    ]);
  });

  const withItem = (item: Record<string, unknown>) => ({ ...snapshot, stations: [{ ...station, items: [{ ...station.items[0], ...item }] }] });
  const withHeld = (consumable: Record<string, unknown>) => ({ ...snapshot, devices: [{ ...device, stockerSlots: [{ stocker: 1, slot: 1, consumable: { ...held, ...consumable } }] }] });

  it.each([
    ['装置の並びがない', { version: 1, stations: [], varieties: [] }],
    ['品種の並びがない', { version: 1, devices: [], stations: [] }],
    ['数が負', { ...snapshot, devices: [{ ...device, shortage: -1 }] }],
    ['スロットの番号が 0', { ...snapshot, devices: [{ ...device, mismatchedSlots: [{ stocker: 1, slot: 0 }] }] }],
    ['品種が空', { ...snapshot, devices: [{ ...device, setupErrors: [''] }] }],
    ['ステーションの ID がない', { ...snapshot, stations: [{ ...station, id: '' }] }],
    ['知らない新品・中古品', withHeld({ condition: 'broken' })],
    ['知らない回収の様子', withHeld({ recovery: { state: 'maybe' } })],
    ['回収が要るのにわけがない', withHeld({ recovery: { state: 'needed', reason: null } })],
    ['入庫した日時が読めない', withItem({ arrivedAt: 'きのう' })],
    ['知らない状態', withItem({ state: { kind: 'lost' } })],
    ['届ける予約の装置がない', withItem({ state: { kind: 'reserved', device: null } })],
    ['知らない要確認のわけ', withItem({ state: { kind: 'needsCheck', reason: 'why' } })],
  ])('形が正しくなければ、例外にする（%s）', (_, value) => {
    expect(() => parseEquipmentStatus(value)).toThrow();
  });
});
