import { describe, expect, it, vi } from 'vitest';
import { agvId } from '../map/agv';
import { heading, panelId } from '../map/model';
import { sampleLayout } from '../map/sampleLayout';
import { click, render } from '../test/render';
import type { AgvDetail } from './AgvCard';
import { MapScreen, type MapScreenProps } from './MapScreen';
import type { Message } from './MessageRail';
import { initialRoles } from './permissions';

const agvs: AgvDetail[] = [
  {
    id: agvId('AGV-1'), label: '1', panel: panelId('P3'), heading: heading(90),
    status: { activity: 'moving', charging: false, carrierMoving: false },
    control: 'auto', operation: 'traveling', transport: '#12 ST-1 から DEV-1 へ', batteryPercent: 72,
  },
  {
    id: agvId('AGV-3'), label: '3', panel: panelId('SB'), heading: heading(90),
    status: { activity: 'idle', charging: false, carrierMoving: false },
    control: 'manual', operation: 'idle', transport: null, batteryPercent: 88,
  },
];

const messages: Message[] = [
  { id: 'm1', time: '10:42', title: '予約が取れずに止まっている', detail: 'AGV-1 が、パネル P4 へ向かう予約を待っています。', unread: true },
];

const accepted = () => Promise.resolve('accepted' as const);

function props(overrides: Partial<MapScreenProps> = {}): MapScreenProps {
  return {
    layout: sampleLayout,
    agvs,
    reservedPanels: [],
    routes: [],
    messages,
    running: true,
    user: { name: '[ユーザー名]', role: '運用' },
    permissions: initialRoles.運用,
    mapVersion: 'マップ 版3（公開済み）',
    destinations: [{ id: panelId('SB'), label: 'SB' }],
    mapViewportSize: { width: 1048, height: 648 },
    onNavigate: () => {},
    onChangePassword: () => {},
    onLogout: () => {},
    onStart: () => {},
    onStop: () => {},
    onMarkAllSeen: () => {},
    onEmergencyStop: () => {},
    onChangeControl: () => {},
    onAlarmClear: () => {},
    onPositionReset: () => {},
    onManualMove: accepted,
    onManualStep: accepted,
    onManualCarrier: accepted,
    ...overrides,
  };
}

const marker = (c: HTMLElement, id: string) => c.querySelector(`[data-agv="${id}"]`)!;
const card = (c: HTMLElement) => c.querySelector('[data-agv-card]');

describe('MapScreen', () => {
  it('上の帯、マップ、運転の切り替えを出す', () => {
    const { container } = render(<MapScreen {...props()} />);

    expect(container.querySelector('header')?.textContent).toContain('FlowHub');
    expect(container.querySelector('svg[aria-label="マップ"]')).not.toBeNull();
    expect(container.querySelector('[role="group"][aria-label="運転状態"]')).not.toBeNull();
  });

  it('まだ見ていないメッセージがあれば、メッセージの列を開いておく。なければ、たたんでおく', () => {
    const withUnread = render(<MapScreen {...props()} />);
    expect(withUnread.container.querySelector('#message-rail')).not.toBeNull();

    const none = render(<MapScreen {...props({ messages: [] })} />);
    expect(none.container.querySelector('#message-rail')).toBeNull();
  });

  it('タブで、メッセージの列を開け閉めする', () => {
    const { container } = render(<MapScreen {...props()} />);
    const tab = () => container.querySelector<HTMLButtonElement>('button[aria-controls="message-rail"]')!;

    click(tab());
    expect(container.querySelector('#message-rail')).toBeNull();
    click(tab());
    expect(container.querySelector('#message-rail')).not.toBeNull();
  });

  it('AGV を押すと、その AGV のカードを出す。ほかの AGV を押すと、そのカードに切り替える', () => {
    const { container } = render(<MapScreen {...props()} />);

    expect(card(container)).toBeNull();
    click(marker(container, 'AGV-1'));
    expect(card(container)?.querySelector('h2')?.textContent).toBe('AGV-1');
    expect(marker(container, 'AGV-1').getAttribute('aria-pressed')).toBe('true');

    click(marker(container, 'AGV-3'));
    expect(card(container)?.querySelector('h2')?.textContent).toBe('AGV-3');
  });

  it('閉じるボタンか、カードの外を押すと、カードを閉じる。カードの中を押しても閉じない', () => {
    const { container } = render(<MapScreen {...props()} />);

    click(marker(container, 'AGV-1'));
    click(card(container)!.querySelector('dl')!);
    expect(card(container)).not.toBeNull();

    click(container.querySelector('header')!);
    expect(card(container)).toBeNull();

    click(marker(container, 'AGV-1'));
    click(card(container)!.querySelector('button[aria-label="閉じる"]')!);
    expect(card(container)).toBeNull();
  });

  it('カードの操作を、その AGV の ID を付けて知らせる', () => {
    const onEmergencyStop = vi.fn();
    const { container } = render(<MapScreen {...props({ onEmergencyStop })} />);

    click(marker(container, 'AGV-1'));
    click([...card(container)!.querySelectorAll('button')].find((b) => b.textContent === '緊急停止')!);

    expect(onEmergencyStop).toHaveBeenCalledWith('AGV-1');
  });

  it('保守のロールで、マニュアルの AGV のカードには、マニュアル操作の欄を出す', () => {
    const { container } = render(<MapScreen {...props({ permissions: initialRoles.保守, user: { name: '[ユーザー名]', role: '保守' } })} />);

    click(marker(container, 'AGV-3'));

    expect(card(container)?.textContent).toContain('マニュアル操作');
    expect(card(container)?.textContent).toContain('この行き先へ動かす');
  });

  it('「すべて見たことにする」を知らせる', () => {
    const onMarkAllSeen = vi.fn();
    const { container } = render(<MapScreen {...props({ onMarkAllSeen })} />);

    click([...container.querySelectorAll('button')].find((b) => b.textContent === 'すべて見たことにする')!);

    expect(onMarkAllSeen).toHaveBeenCalledTimes(1);
  });
});
