import { describe, expect, it, vi } from 'vitest';
import { click, pointer, render, wheel } from '../test/render';
import { agvId, type AgvOnMap } from './agv';
import { MapViewport } from './MapViewport';
import { heading, panelId } from './model';
import { sampleEquipment, sampleLayout } from './sampleLayout';

// 見本のマップは 6000 x 4000 mm。余白 24px を除いた 1000 x 600 px に収めると、0.15 px/mm
const size = { width: 1048, height: 648 };
const agvs: AgvOnMap[] = [
  { id: agvId('AGV-1'), label: '1', panel: panelId('P3'), heading: heading(90), status: { activity: 'moving', charging: false, carrierMoving: false } },
];

function viewBox(container: HTMLElement): number[] {
  return (container.querySelector('svg')?.getAttribute('viewBox') ?? '').split(' ').map(Number);
}

function button(container: HTMLElement, label: string): HTMLElement {
  const found = container.querySelector<HTMLElement>(`button[aria-label="${label}"]`);
  if (!found) {
    throw new Error(`「${label}」のボタンがありません`);
  }
  return found;
}

function surface(container: HTMLElement): HTMLElement {
  const found = container.querySelector<HTMLElement>('[data-map-surface]');
  if (!found) {
    throw new Error('マップの面がありません');
  }
  return found;
}

describe('MapViewport', () => {
  it('最初は、マップの全体が収まる大きさで、真ん中に置く', () => {
    const { container } = render(<MapViewport layout={sampleLayout} size={size} />);
    const [x, y, w, h] = viewBox(container);

    expect(w).toBeCloseTo(1048 / 0.15);
    expect(h).toBeCloseTo(648 / 0.15);
    expect(x! + w! / 2).toBeCloseTo(3000);
    expect(y! + h! / 2).toBeCloseTo(2000);
  });

  it('装置とステーションがあれば、その外形まで含めて全体を収める', () => {
    const { container } = render(<MapViewport layout={sampleLayout} size={size} equipment={sampleEquipment} />);
    const [x, y, w, h] = viewBox(container);

    // 外形まで含めると、x は 0〜6000、y は -750〜5000（真ん中は 3000, 2125）
    expect(x! + w! / 2).toBeCloseTo(3000);
    expect(y! + h! / 2).toBeCloseTo(2125);
  });

  it('「拡大する」「縮小する」で、画面の真ん中を中心に大きさを変える', () => {
    const { container } = render(<MapViewport layout={sampleLayout} size={size} />);
    const [, , before] = viewBox(container);

    click(button(container, '拡大する'));
    const [x, , zoomed] = viewBox(container);
    expect(zoomed).toBeCloseTo(before! / 1.25);
    expect(x! + zoomed! / 2).toBeCloseTo(3000);

    click(button(container, '縮小する'));
    expect(viewBox(container)[2]).toBeCloseTo(before!);
  });

  it('「表示の位置を戻す」で、全体が収まる大きさに戻す', () => {
    const { container } = render(<MapViewport layout={sampleLayout} size={size} />);
    const fit = viewBox(container);

    click(button(container, '拡大する'));
    pointer(surface(container), 'pointerdown', { x: 100, y: 100 });
    pointer(surface(container), 'pointermove', { x: 160, y: 130 });
    pointer(surface(container), 'pointerup', { x: 160, y: 130 });
    click(button(container, '表示の位置を戻す'));

    expect(viewBox(container)).toEqual(fit);
  });

  it('ドラッグした向きに、マップを動かす', () => {
    const { container } = render(<MapViewport layout={sampleLayout} size={size} />);
    const [x0, y0] = viewBox(container);

    pointer(surface(container), 'pointerdown', { x: 100, y: 100 });
    pointer(surface(container), 'pointermove', { x: 130, y: 115 });
    pointer(surface(container), 'pointerup', { x: 130, y: 115 });

    const [x1, y1] = viewBox(container);
    expect(x1).toBeCloseTo(x0! - 30 / 0.15);
    expect(y1).toBeCloseTo(y0! - 15 / 0.15);
  });

  it('ホイールを奥へ回すと拡大し、手前へ回すと縮小する', () => {
    const { container } = render(<MapViewport layout={sampleLayout} size={size} />);
    const [, , before] = viewBox(container);

    wheel(surface(container), -100, { x: 524, y: 324 });
    const [, , zoomedIn] = viewBox(container);
    expect(zoomedIn).toBeLessThan(before!);

    wheel(surface(container), 100, { x: 524, y: 324 });
    wheel(surface(container), 100, { x: 524, y: 324 });
    expect(viewBox(container)[2]).toBeGreaterThan(before!);
  });

  it('ドラッグしたあとは、指を離したところの AGV を選ばない', () => {
    const onSelectAgv = vi.fn();
    const { container } = render(<MapViewport layout={sampleLayout} size={size} agvs={agvs} onSelectAgv={onSelectAgv} />);
    const agv = container.querySelector('[data-agv="AGV-1"]')!;

    pointer(surface(container), 'pointerdown', { x: 100, y: 100 });
    pointer(surface(container), 'pointermove', { x: 140, y: 100 });
    pointer(surface(container), 'pointerup', { x: 140, y: 100 });
    click(agv);

    expect(onSelectAgv).not.toHaveBeenCalled();
  });

  it('少し動かしただけ（4px まで）なら、押したことにして AGV を選ぶ', () => {
    const onSelectAgv = vi.fn();
    const { container } = render(<MapViewport layout={sampleLayout} size={size} agvs={agvs} onSelectAgv={onSelectAgv} />);
    const agv = container.querySelector('[data-agv="AGV-1"]')!;
    const [x0] = viewBox(container);

    pointer(surface(container), 'pointerdown', { x: 100, y: 100 });
    pointer(surface(container), 'pointermove', { x: 103, y: 101 });
    pointer(surface(container), 'pointerup', { x: 103, y: 101 });
    click(agv);

    expect(onSelectAgv).toHaveBeenCalledWith('AGV-1');
    expect(viewBox(container)[0]).toBeCloseTo(x0!);
  });

  it('隠したくない点（focus）がカードに覆われていたら、見えるところまで動かし、focus がなくなったら元に戻す', () => {
    const covered = { left: 380, bottom: 0 };
    const sb = { x: 500, y: 1500 }; // SB の十字（左の端）
    const { container, rerender } = render(<MapViewport layout={sampleLayout} size={size} covered={covered} focus={null} />);
    const fit = viewBox(container);

    rerender(<MapViewport layout={sampleLayout} size={size} covered={covered} focus={sb} />);
    const moved = viewBox(container);
    expect(moved).not.toEqual(fit);
    // 覆われていない範囲（380〜1048px）の真ん中あたりに来る
    const sx = ((sb.x - moved[0]!) / moved[2]!) * size.width;
    expect(sx).toBeCloseTo((380 + 1048) / 2);

    rerender(<MapViewport layout={sampleLayout} size={size} covered={covered} focus={null} />);
    expect(viewBox(container)).toEqual(fit);
  });

  it('見えている点なら、動かさない', () => {
    const covered = { left: 380, bottom: 0 };
    const { container, rerender } = render(<MapViewport layout={sampleLayout} size={size} covered={covered} focus={null} />);
    const fit = viewBox(container);

    rerender(<MapViewport layout={sampleLayout} size={size} covered={covered} focus={{ x: 4500, y: 1500 }} />);

    expect(viewBox(container)).toEqual(fit);
  });

  it('動かしたあとに自分でマップを動かしたら、focus がなくなっても戻さない', () => {
    const covered = { left: 380, bottom: 0 };
    const { container, rerender } = render(<MapViewport layout={sampleLayout} size={size} covered={covered} focus={null} />);

    rerender(<MapViewport layout={sampleLayout} size={size} covered={covered} focus={{ x: 500, y: 1500 }} />);
    pointer(surface(container), 'pointerdown', { x: 600, y: 300 });
    pointer(surface(container), 'pointermove', { x: 650, y: 300 });
    pointer(surface(container), 'pointerup', { x: 650, y: 300 });
    const dragged = viewBox(container);

    rerender(<MapViewport layout={sampleLayout} size={size} covered={covered} focus={null} />);
    expect(viewBox(container)).toEqual(dragged);
  });

  it('focus をほかの点に変えても、なくなったときは、最初に動かす前の位置に戻す', () => {
    const covered = { left: 380, bottom: 0 };
    const { container, rerender } = render(<MapViewport layout={sampleLayout} size={size} covered={covered} focus={null} />);
    const fit = viewBox(container);

    rerender(<MapViewport layout={sampleLayout} size={size} covered={covered} focus={{ x: 500, y: 1500 }} />);
    rerender(<MapViewport layout={sampleLayout} size={size} covered={covered} focus={{ x: 1500, y: 3500 }} />);
    rerender(<MapViewport layout={sampleLayout} size={size} covered={covered} focus={null} />);

    expect(viewBox(container)).toEqual(fit);
  });

  it('2本の指でつまむと、指の間を広げた割合だけ拡大する', () => {
    const { container } = render(<MapViewport layout={sampleLayout} size={size} />);
    const [, , before] = viewBox(container);
    const el = surface(container);

    pointer(el, 'pointerdown', { x: 400, y: 300 }, 1);
    pointer(el, 'pointerdown', { x: 500, y: 300 }, 2);
    pointer(el, 'pointermove', { x: 530, y: 300 }, 2);

    // 指の間が 100px から 130px に広がったので、1.3倍
    expect(viewBox(container)[2]).toBeCloseTo(before! / 1.3);
  });
});
