import { describe, expect, it } from 'vitest';
import { equipmentId } from '../map/equipment';
import { panelId } from '../map/model';
import { chooseDevice, settingProblem, slotCapacity, toSaved, type DeviceInside, type Stocker } from './devices';

const slots = (name: string, count: number): Stocker => ({
  name,
  layout: 'slots',
  slots: Array.from({ length: count }, (_, i) => ({ number: i + 1, content: { kind: 'empty' } })),
});

function device(id: string, stockers: readonly Stocker[] = [slots('ストッカー1', 3)]): DeviceInside {
  return { id: equipmentId(id), panel: panelId('D1'), processing: null, stockers, handover: null, setting: [] };
}

describe('chooseDevice（出す装置）', () => {
  const devices = [device('DEV-10'), device('DEV-2'), device('DEV-1')];

  it('指定した装置があれば、それを出す', () => {
    expect(chooseDevice(devices, 'DEV-2')?.id).toBe('DEV-2');
  });

  it('指定がないときや、ない装置のときは、番号がいちばん小さい装置', () => {
    expect(chooseDevice(devices, null)?.id).toBe('DEV-1');
    expect(chooseDevice(devices, 'DEV-99')?.id).toBe('DEV-1');
  });

  it('装置がなければ null', () => {
    expect(chooseDevice([], null)).toBeNull();
  });
});

describe('slotCapacity（スロットの数）', () => {
  it('すべてのストッカーのスロットを足す', () => {
    expect(slotCapacity(device('DEV-1', [slots('ストッカー1', 3), slots('ストッカー2', 3)]))).toBe(6);
  });

  it('プレートに載せたまま置くストッカーがあれば、上限なし（null）', () => {
    expect(slotCapacity(device('DEV-1', [{ name: 'ストッカー1', layout: 'plate', items: [] }]))).toBeNull();
  });
});

describe('settingProblem（保存できない理由）', () => {
  it('合計がスロットの数を超えたら保存できない', () => {
    const draft = [
      { variety: '[品種A]', target: '4' },
      { variety: '[品種B]', target: '3' },
    ];

    expect(settingProblem(draft, 6)).toBe('合計 7個が、スロットの数（6）を超えています。');
    expect(settingProblem(draft, 7)).toBeNull();
  });

  it('上限がない（プレート）ときは、合計を確かめない', () => {
    expect(settingProblem([{ variety: '[品種A]', target: '40' }], null)).toBeNull();
  });

  it('個数は 0 以上の整数でなければ保存できない', () => {
    expect(settingProblem([{ variety: '[品種A]', target: '' }], 6)).toBe('[品種A] の個数を、0 以上の整数で入れてください。');
    expect(settingProblem([{ variety: '[品種A]', target: '-1' }], 6)).not.toBeNull();
    expect(settingProblem([{ variety: '[品種A]', target: '1.5' }], 6)).not.toBeNull();
    expect(settingProblem([{ variety: '[品種A]', target: '0' }], 6)).toBeNull();
  });
});

describe('toSaved（保存する設定）', () => {
  it('0個の品種は、設定から外す', () => {
    expect(
      toSaved([
        { variety: '[品種A]', target: '2' },
        { variety: '[品種B]', target: '0' },
      ]),
    ).toEqual([{ variety: '[品種A]', target: 2 }]);
  });
});
