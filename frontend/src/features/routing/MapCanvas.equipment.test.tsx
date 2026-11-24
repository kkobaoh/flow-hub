import { describe, expect, it, vi } from 'vitest';
import { click, hover, pressKey, render } from '../../testing/render';
import { equipmentId } from '../tool/equipment';
import { MapCanvas } from './MapCanvas';
import { sampleEquipment, sampleLayout } from '../../app/demo/sampleLayout';

function el(container: HTMLElement, id: string): Element {
  const found = container.querySelector(`[data-equipment="${id}"]`);
  if (!found) {
    throw new Error(`${id} がありません`);
  }
  return found;
}

describe('MapCanvas の装置とステーション', () => {
  it('外形を、受け渡しパネルに重ねて描く（床の下にあるので、点線で透かす）', () => {
    const { container } = render(<MapCanvas layout={sampleLayout} equipment={sampleEquipment} />);
    const outline = el(container, 'DEV-1').querySelector('[data-outline]');

    expect(outline?.getAttribute('stroke-dasharray')).not.toBeNull();
    expect(outline?.getAttribute('x')).toBe('3050');
    expect(outline?.getAttribute('width')).toBe('900');
  });

  it('押せる印にし、名前と気をつけることを読み上げる', () => {
    const { container } = render(<MapCanvas layout={sampleLayout} equipment={sampleEquipment} />);

    expect(el(container, 'DEV-1').getAttribute('role')).toBe('button');
    expect(el(container, 'DEV-1').getAttribute('aria-label')).toBe('DEV-1（回収待ち 2、ID 食い違い）');
    expect(el(container, 'DEV-3').getAttribute('aria-label')).toBe('DEV-3');
  });

  it('気をつけることがあれば、札を出す', () => {
    const { container } = render(<MapCanvas layout={sampleLayout} equipment={sampleEquipment} />);

    expect([...el(container, 'DEV-1').querySelectorAll('[data-tag]')].map((t) => t.textContent)).toEqual(['回収待ち 2', 'ID 食い違い']);
    expect(el(container, 'DEV-3').querySelectorAll('[data-tag]')).toHaveLength(0);
  });

  it('押すと選び、マウスを乗せたことを知らせる', () => {
    const onSelectEquipment = vi.fn();
    const onHighlightEquipment = vi.fn();
    const { container } = render(
      <MapCanvas layout={sampleLayout} equipment={sampleEquipment} onSelectEquipment={onSelectEquipment} onHighlightEquipment={onHighlightEquipment} />,
    );

    click(el(container, 'ST-1'));
    pressKey(el(container, 'DEV-2'), 'Enter');
    hover(el(container, 'DEV-1'), true);
    hover(el(container, 'DEV-1'), false);

    expect(onSelectEquipment.mock.calls).toEqual([['ST-1'], ['DEV-2']]);
    expect(onHighlightEquipment.mock.calls).toEqual([['DEV-1'], [null]]);
  });

  it('選んでいるものと、マウスを乗せているものを見分けられるようにする', () => {
    const { container } = render(
      <MapCanvas
        layout={sampleLayout}
        equipment={sampleEquipment}
        selectedEquipment={equipmentId('DEV-1')}
        highlightedEquipment={equipmentId('ST-1')}
      />,
    );

    expect(el(container, 'DEV-1').getAttribute('data-emphasis')).toBe('selected');
    expect(el(container, 'DEV-1').getAttribute('aria-pressed')).toBe('true');
    expect(el(container, 'ST-1').getAttribute('data-emphasis')).toBe('highlighted');
  });

  it('描く範囲を、外形まで広げる', () => {
    const { container } = render(<MapCanvas layout={sampleLayout} equipment={sampleEquipment} />);
    const [x, y] = (container.querySelector('svg')?.getAttribute('viewBox') ?? '').split(' ').map(Number);

    expect(y).toBeLessThan(0); // ST-1 はマップの北の外まではみ出す
    expect(x).toBe(0);
  });
});
