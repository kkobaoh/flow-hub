import { describe, expect, it } from 'vitest';
import { sampleLayout } from '../map/sampleLayout';
import { panelOptions } from './panelOptions';

describe('panelOptions（行き先と位置で選べるパネル）', () => {
  it('マップのパネルを、ID の順に、種類がわかる名前で並べる', () => {
    const options = panelOptions(sampleLayout);

    expect(options.map((o) => o.id)).toEqual(['D1', 'D2', 'P1', 'P2', 'P3', 'P4', 'P5', 'P6', 'P7', 'P8', 'SB', 'SB2', 'SP', 'ST']);
    expect(options.find((o) => o.id === 'P1')?.label).toBe('P1');
    expect(options.find((o) => o.id === 'ST')?.label).toBe('ST（受け渡しパネル）');
    expect(options.find((o) => o.id === 'SB')?.label).toBe('SB（待機パネル）');
    expect(options.find((o) => o.id === 'SB2')?.label).toBe('SB2（待機パネル・充電ピンあり）');
  });

  it('特殊消耗品受け渡しパネルは、着いたときの向きを、キャリアを下ろす角度に決めておく', () => {
    const sp = panelOptions(sampleLayout).find((o) => o.id === 'SP');

    expect(sp).toEqual({ id: 'SP', label: 'SP（特殊消耗品受け渡しパネル）', fixedArrival: 45 });
  });
});
