import { describe, expect, it } from 'vitest';
import { panelId } from '../routing/model';
import type { DeviceStatus, EquipmentStatusSnapshot, StationStatus } from './api/equipmentStatus';
import { equipmentId, type EquipmentOnMap } from './equipment';
import { equipmentDetails } from './equipmentDetails';

const onMap = (id: string, kind: 'device' | 'station', panel: string): EquipmentOnMap => ({
  id: equipmentId(id),
  kind,
  panel: panelId(panel),
  outlineMm: { x: 10, y: 20, width: 30, height: 40 },
  attentions: [],
});

const device: DeviceStatus = {
  id: 'DEV-1',
  panel: 'D1',
  shortage: 2,
  collectWaiting: 1,
  mismatchedSlots: [{ stocker: 1, slot: 3 }],
  setupErrors: ['DR-8', 'DR-9'],
  stocker: [{ variety: 'DR-5', target: 2, have: 1 }],
  slots: { total: 10, free: 7, delivering: 1 },
  processing: [],
  stockerSlots: [],
  handover: [],
};
const station: StationStatus = { id: 'ST-1', panel: 'ST', needsCheck: 1, replenishWaiting: 2, stock: [{ variety: 'DR-5', available: 3, reserved: 0, replenishWaiting: 2 }], items: [] };
const statuses = (devices: DeviceStatus[], stations: StationStatus[]): EquipmentStatusSnapshot => ({ version: 1, devices, stations, varieties: [] });

describe('equipmentDetails（マップに描き、カードに出す装置とステーション）', () => {
  it('位置と外形はマップの、気をつけることと中身は装置の様子のものを使う', () => {
    const [dev] = equipmentDetails([onMap('DEV-1', 'device', 'D1')], statuses([device], []));

    expect(dev).toMatchObject({
      id: 'DEV-1',
      panel: 'D1',
      outlineMm: { x: 10, y: 20, width: 30, height: 40 },
      attentions: [
        { kind: 'shortage', count: 2 },
        { kind: 'collectWaiting', count: 1 },
        { kind: 'idMismatch', count: 1 },
        { kind: 'setupError', count: 2 },
      ],
      stocker: [{ variety: 'DR-5', target: 2, have: 1 }],
      slots: { total: 10, free: 7, delivering: 1 },
    });
    expect(dev?.details.idMismatch).toContain('ストッカー1・スロット3');
    expect(dev?.details.setupError).toContain('DR-8、DR-9');
  });

  it('数が 0 の気をつけることは出さない', () => {
    const quiet = { ...device, shortage: 0, collectWaiting: 0, mismatchedSlots: [], setupErrors: [] };

    expect(equipmentDetails([onMap('DEV-1', 'device', 'D1')], statuses([quiet], []))[0]?.attentions).toEqual([]);
  });

  it('ステーションは、要確認と補充待ちと品種ごとの数を出す', () => {
    const [st] = equipmentDetails([onMap('ST-1', 'station', 'ST')], statuses([], [station]));

    expect(st).toMatchObject({
      kind: 'station',
      attentions: [
        { kind: 'needsCheck', count: 1 },
        { kind: 'replenishWaiting', count: 2 },
      ],
      stock: [{ variety: 'DR-5', available: 3, reserved: 0, replenishWaiting: 2 }],
    });
  });

  it('様子がまだ届いていない装置とステーションは、中身を空にする', () => {
    const [dev, st] = equipmentDetails([onMap('DEV-9', 'device', 'D1'), onMap('ST-1', 'station', 'ST')], null);

    expect(dev).toMatchObject({ kind: 'device', attentions: [], stocker: [], slots: { total: 0, free: 0, delivering: 0 }, details: {} });
    expect(st).toMatchObject({ kind: 'station', attentions: [], stock: [], details: {} });
  });
});
