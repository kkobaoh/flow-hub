import { describe, expect, it } from 'vitest';
import { equipmentId, type EquipmentOnMap } from './equipment';
import { panelId } from '../routing/model';
import { demoEquipment } from '../../app/demo/demo';
import { equipmentDetails } from './equipmentDetails';

const onMap = (id: string, kind: 'device' | 'station', panel: string): EquipmentOnMap => ({
  id: equipmentId(id),
  kind,
  panel: panelId(panel),
  outlineMm: { x: 10, y: 20, width: 30, height: 40 },
  attentions: [],
});

describe('equipmentDetails（カードに出す装置とステーション）', () => {
  it('位置と外形はサーバーのものを使い、中身は見本のデータを ID で合わせて使う（気をつけることは、まだ空）', () => {
    const [dev1] = equipmentDetails([onMap('DEV-1', 'device', 'D9')], demoEquipment);

    expect(dev1).toMatchObject({ id: 'DEV-1', kind: 'device', panel: 'D9', outlineMm: { x: 10, y: 20, width: 30, height: 40 }, attentions: [] });
    expect(dev1?.kind === 'device' && dev1.stocker.length).toBeGreaterThan(0);
  });

  it('見本にない装置とステーションは、中身を空にする', () => {
    const [device, station] = equipmentDetails([onMap('DEV-9', 'device', 'D1'), onMap('ST-9', 'station', 'ST')], demoEquipment);

    expect(device).toMatchObject({ kind: 'device', stocker: [], slots: { total: 0, free: 0, delivering: 0 }, details: {} });
    expect(station).toMatchObject({ kind: 'station', stock: [], details: {} });
  });
});
