import { describe, expect, it, vi } from 'vitest';
import { click, hover, pressKey, render } from '../test/render';
import { agvId, type AgvOnMap } from './agv';
import { MapCanvas } from './MapCanvas';
import { heading, panelId } from './model';
import { sampleLayout } from './sampleLayout';

const agvs: AgvOnMap[] = [
  { id: agvId('AGV-1'), label: '1', panel: panelId('P3'), heading: heading(90), status: { activity: 'moving', charging: false, carrierMoving: false } },
  { id: agvId('AGV-2'), label: '2', panel: panelId('SB2'), heading: heading(90), status: { activity: 'idle', charging: true, carrierMoving: false } },
];

function marker(container: HTMLElement, id: string): Element {
  const found = container.querySelector(`[data-agv="${id}"]`);
  if (!found) {
    throw new Error(`${id} の印がありません`);
  }
  return found;
}

describe('MapCanvas の AGV', () => {
  it('AGV を、いるパネルの十字に、向きに合わせて描く', () => {
    const { container } = render(<MapCanvas layout={sampleLayout} agvs={agvs} />);

    // P3 は列3・行1 なので、十字は (3500, 1500)
    expect(marker(container, 'AGV-1').getAttribute('transform')).toBe('translate(3500 1500) rotate(90)');
  });

  it('押せる印にし、名前と標示灯の状態を読み上げる', () => {
    const { container } = render(<MapCanvas layout={sampleLayout} agvs={agvs} />);
    const m = marker(container, 'AGV-2');

    expect(m.getAttribute('role')).toBe('button');
    expect(m.getAttribute('tabindex')).toBe('0');
    expect(m.getAttribute('aria-label')).toBe('AGV-2（充電中）');
  });

  it('標示灯の色と点滅を、AGV の状態と、いるパネルの充電ピンから決める', () => {
    const { container } = render(<MapCanvas layout={sampleLayout} agvs={agvs} />);

    const light1 = marker(container, 'AGV-1').querySelector('[data-light]');
    expect(light1?.getAttribute('data-light')).toBe('moving');
    expect(light1?.getAttribute('class')).toContain('animate-agv-blink');

    const light2 = marker(container, 'AGV-2').querySelector('[data-light]');
    expect(light2?.getAttribute('data-light')).toBe('charging');
    expect(light2?.getAttribute('class')).not.toContain('animate-agv-blink');
  });

  it('押すと、その AGV を選ぶ（Enter と Space でも選べる）', () => {
    const onSelectAgv = vi.fn();
    const { container } = render(<MapCanvas layout={sampleLayout} agvs={agvs} onSelectAgv={onSelectAgv} />);

    click(marker(container, 'AGV-1'));
    pressKey(marker(container, 'AGV-2'), 'Enter');
    pressKey(marker(container, 'AGV-2'), ' ');

    expect(onSelectAgv.mock.calls).toEqual([['AGV-1'], ['AGV-2'], ['AGV-2']]);
  });

  it('選んでいる AGV と、マウスを乗せている AGV を見分けられるようにする', () => {
    const { container } = render(
      <MapCanvas layout={sampleLayout} agvs={agvs} selectedAgv={agvId('AGV-1')} highlightedAgv={agvId('AGV-2')} />,
    );

    expect(marker(container, 'AGV-1').getAttribute('aria-pressed')).toBe('true');
    expect(marker(container, 'AGV-1').getAttribute('data-emphasis')).toBe('selected');
    expect(marker(container, 'AGV-2').getAttribute('aria-pressed')).toBe('false');
    expect(marker(container, 'AGV-2').getAttribute('data-emphasis')).toBe('highlighted');
  });

  it('マウスを乗せたり外したりしたことを知らせる', () => {
    const onHighlightAgv = vi.fn();
    const { container } = render(<MapCanvas layout={sampleLayout} agvs={agvs} onHighlightAgv={onHighlightAgv} />);

    hover(marker(container, 'AGV-1'), true);
    hover(marker(container, 'AGV-1'), false);

    expect(onHighlightAgv.mock.calls).toEqual([['AGV-1'], [null]]);
  });

  it('マップにないパネルにいる AGV は、呼び出す側の誤りとして例外にする', () => {
    const lost: AgvOnMap = { ...agvs[0]!, panel: panelId('ZZ') };

    expect(() => render(<MapCanvas layout={sampleLayout} agvs={[lost]} />)).toThrow(Error);
  });
});

describe('MapCanvas の予約と経路', () => {
  it('予約しているパネルに印を付ける', () => {
    const { container } = render(<MapCanvas layout={sampleLayout} reservedPanels={[panelId('P3'), panelId('P4')]} />);

    expect(container.querySelectorAll('[data-reserved]')).toHaveLength(2);
  });

  it('走る予定の経路を、十字と十字を結んで描く', () => {
    const route = [panelId('P3'), panelId('P4'), panelId('P5'), panelId('P6'), panelId('D1')];
    const { container } = render(<MapCanvas layout={sampleLayout} routes={[route]} />);

    expect(container.querySelectorAll('[data-route]')).toHaveLength(4);
  });
});
