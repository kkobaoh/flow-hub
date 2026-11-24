import { describe, expect, it } from 'vitest';
import { parseEquipmentStatus } from './equipmentStatus';

const device = {
  id: 'DEV-1',
  shortage: 2,
  collectWaiting: 1,
  mismatchedSlots: [{ stocker: 1, slot: 3 }],
  setupErrors: ['DR-8'],
  stocker: [{ variety: 'DR-5', target: 2, have: 1 }],
  slots: { total: 10, free: 7, delivering: 1 },
};
const station = { id: 'ST-1', needsCheck: 1, replenishWaiting: 2, stock: [{ variety: 'DR-5', available: 3, replenishWaiting: 2 }] };

describe('parseEquipmentStatus', () => {
  it('サーバーから届いた装置とステーションの様子を、型にする', () => {
    const parsed = parseEquipmentStatus({ version: 4, devices: [device], stations: [station] });

    expect(parsed).toEqual({ version: 4, devices: [device], stations: [station] });
  });

  it.each([
    ['装置の並びがない', { version: 1, stations: [] }],
    ['数が負', { version: 1, devices: [{ ...device, shortage: -1 }], stations: [] }],
    ['スロットの番号が 0', { version: 1, devices: [{ ...device, mismatchedSlots: [{ stocker: 1, slot: 0 }] }], stations: [] }],
    ['品種が空', { version: 1, devices: [{ ...device, setupErrors: [''] }], stations: [] }],
    ['ステーションの ID がない', { version: 1, devices: [], stations: [{ ...station, id: '' }] }],
  ])('形が正しくなければ、例外にする（%s）', (_, value) => {
    expect(() => parseEquipmentStatus(value)).toThrow();
  });
});
