import { describe, expect, it } from 'vitest';
import { agvId } from '../map/agv';
import { heading, panelId } from '../map/model';
import type { AgvDetail } from './AgvCard';
import { placedAgvs, unplacedAgvs } from './agvPlacement';

const base: Omit<AgvDetail, 'id' | 'label' | 'position'> = {
  status: { activity: 'idle', charging: 'unknown', carrierMoving: false },
  control: 'auto',
  operation: 'idle',
  transport: null,
  batteryPercent: null,
};
const onMap: AgvDetail = { ...base, id: agvId('AGV-1'), label: 'AGV-1', position: { kind: 'onMap', panel: panelId('P3'), heading: heading(90) } };
const unknown: AgvDetail = { ...base, id: agvId('AGV-2'), label: 'AGV-2', position: { kind: 'unknown' } };
const offMap: AgvDetail = { ...base, id: agvId('AGV-3'), label: 'AGV-3', position: { kind: 'offMap', panel: panelId('P99'), heading: heading(0) } };

describe('placedAgvs と unplacedAgvs', () => {
  it('マップの上にいる AGV だけを、マップに描く印にする', () => {
    expect(placedAgvs([onMap, unknown, offMap])).toEqual([{ id: 'AGV-1', label: 'AGV-1', panel: 'P3', heading: 90, status: onMap.status }]);
  });

  it('位置がわからない AGV と、マップにないパネルにいる AGV を、下の列の印にする（並びはそのまま）', () => {
    expect(unplacedAgvs([onMap, unknown, offMap]).map((a) => a.id)).toEqual(['AGV-2', 'AGV-3']);
  });
});
