import { describe, expect, it } from 'vitest';
import { parseMap } from './mapSnapshot';

const wire = {
  version: 2,
  mapVersion: 3,
  panelSizeMm: 1000,
  panels: [
    { id: 'P1', type: 'Normal', hasChargingPin: false, carrierLoweringHeading: null, column: 1, row: 1, crossOffsetEastMm: 0, crossOffsetSouthMm: 0 },
    { id: 'SP', type: 'SpecialTransfer', hasChargingPin: false, carrierLoweringHeading: 45, column: 2, row: 1, crossOffsetEastMm: 120, crossOffsetSouthMm: -40 },
  ],
  connections: [{ from: 'P1', to: 'SP', distanceMm: 1000, heading: 90, oneWay: false }],
  equipment: [{ id: 'DEV-3', kind: 'device', panel: 'SP', outline: { xMm: 1500, yMm: 1900, widthMm: 900, heightMm: 800 } }],
};

describe('parseMap', () => {
  it('サーバーから届いたマップを、描く形（パネル、つながり、装置とステーション）にする', () => {
    const map = parseMap(wire);

    expect(map.version).toBe(2);
    expect(map.mapVersion).toBe(3);
    expect(map.layout.panelSizeMm).toBe(1000);
    expect(map.layout.panels[1]).toEqual({ id: 'SP', type: 'SpecialTransfer', hasChargingPin: false, carrierLoweringHeading: 45, column: 2, row: 1, crossOffsetMm: { x: 120, y: -40 } });
    expect(map.layout.panels[0]).not.toHaveProperty('carrierLoweringHeading');
    expect(map.layout.connections).toEqual([{ from: 'P1', to: 'SP', distanceMm: 1000, heading: 90, oneWay: false }]);
    expect(map.equipment).toEqual([{ id: 'DEV-3', kind: 'device', panel: 'SP', outlineMm: { x: 1500, y: 1900, width: 900, height: 800 }, attentions: [] }]);
  });

  it('まだ公開していない（版が 0）ときは、パネルのないマップ', () => {
    const map = parseMap({ ...wire, mapVersion: 0, panelSizeMm: 0, panels: [], connections: [], equipment: [] });

    expect(map.mapVersion).toBe(0);
    expect(map.layout.panels).toEqual([]);
  });

  it.each([
    ['パネルの並びがない', { ...wire, panels: undefined }],
    ['知らない種類のパネル', { ...wire, panels: [{ ...wire.panels[0], type: 'Hole' }] }],
    ['列が整数でない', { ...wire, panels: [{ ...wire.panels[0], column: 1.5 }] }],
    ['向きが範囲の外', { ...wire, connections: [{ ...wire.connections[0], heading: 360 }] }],
    ['知らない装置の種類', { ...wire, equipment: [{ ...wire.equipment[0], kind: 'robot' }] }],
    ['外形の幅が 0', { ...wire, equipment: [{ ...wire.equipment[0], outline: { xMm: 0, yMm: 0, widthMm: 0, heightMm: 10 } }] }],
    ['パネルの一辺が 0 なのにパネルがある', { ...wire, panelSizeMm: 0 }],
  ])('形が正しくなければ、例外にする（%s）', (_, value) => {
    expect(() => parseMap(value)).toThrow();
  });
});
