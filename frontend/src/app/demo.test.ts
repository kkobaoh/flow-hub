import { describe, expect, it } from 'vitest';
import { applyDemoSetting, demoDevices } from './demo';

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
