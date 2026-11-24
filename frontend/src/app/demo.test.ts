import { describe, expect, it } from 'vitest';
import { applyDemoSetting, demoDevices, demoMapDraft, demoValidate } from './demo';

describe('applyDemoSetting（見本の画面で、保存した設定を当てる）', () => {
  it('品種ごとに、ある数と比べて様子を決め、外した品種で装置にあるものは「設定にない」にする', () => {
    const dev1 = demoDevices.find((d) => d.id === 'DEV-1')!;

    const after = applyDemoSetting(dev1, [
      { variety: '[品種A]', target: 3 },
      { variety: '[品種C]', target: 1 },
    ]);

    expect(after.setting).toEqual([
      { variety: '[品種A]', target: 3, have: 2, status: 'short' },
      { variety: '[品種C]', target: 1, have: 0, status: 'short' },
      { variety: '[品種B]', target: 0, have: 1, status: 'notInSetting' },
      { variety: '[品種D]', target: 0, have: 1, status: 'notInSetting' },
    ]);
  });
});

describe('demoValidate（見本の画面で、下書きを確かめる）', () => {
  it('見本の下書きには問題がなく、角度を外したり、つながりを外したりすると問題を出す', () => {
    expect(demoValidate(demoMapDraft)).toEqual([]);

    const noAngle = { ...demoMapDraft, layout: { ...demoMapDraft.layout, panels: demoMapDraft.layout.panels.map(({ carrierLoweringHeading: _a, ...p }) => p) } };
    expect(demoValidate(noAngle)).toContain('SP に、キャリアを下ろす角度が入っていません。');

    const cut = { ...demoMapDraft, layout: { ...demoMapDraft.layout, connections: demoMapDraft.layout.connections.filter((c) => c.from !== 'ST' && c.to !== 'ST') } };
    expect(demoValidate(cut)).toContain('ST は行き止まりのパネルなので、つながりを1本だけにしてください（いまは 0本）。');
  });
});
