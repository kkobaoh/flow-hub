import { describe, expect, it } from 'vitest';
import { demoMapDraft, demoValidate } from './demo';

describe('demoValidate（見本の画面で、下書きを確かめる）', () => {
  it('見本の下書きには問題がなく、角度を外したり、つながりを外したりすると問題を出す', () => {
    expect(demoValidate(demoMapDraft)).toEqual([]);

    const noAngle = { ...demoMapDraft, layout: { ...demoMapDraft.layout, panels: demoMapDraft.layout.panels.map(({ carrierLoweringHeading: _a, ...p }) => p) } };
    expect(demoValidate(noAngle)).toContain('SP に、キャリアを下ろす角度が入っていません。');

    const cut = { ...demoMapDraft, layout: { ...demoMapDraft.layout, connections: demoMapDraft.layout.connections.filter((c) => c.from !== 'ST' && c.to !== 'ST') } };
    expect(demoValidate(cut)).toContain('ST は行き止まりのパネルなので、つながりを1本だけにしてください（いまは 0本）。');
  });
});
