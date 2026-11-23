import { describe, expect, it, vi } from 'vitest';
import { click, render } from '../test/render';
import { demoMessages } from './demo';
import { initialRoles } from './permissions';
import { ScreenFrame, type ScreenFrameProps } from './ScreenFrame';

function props(overrides: Partial<ScreenFrameProps> = {}): ScreenFrameProps {
  return {
    current: 'transports',
    title: '搬送の一覧',
    running: true,
    user: { name: '[ユーザー名]', role: '運用' },
    permissions: initialRoles.運用,
    mapVersion: 'マップ 版3（公開済み）',
    messages: demoMessages,
    onMarkAllSeen: () => {},
    onNavigate: () => {},
    onChangePassword: () => {},
    onLogout: () => {},
    children: <p id="content">中身</p>,
    ...overrides,
  };
}

const tab = (c: HTMLElement) => c.querySelector<HTMLButtonElement>('button[aria-controls="message-rail"]')!;

describe('ScreenFrame（マップ以外の画面の枠）', () => {
  it('上の帯、画面の見出し、中身を出す', () => {
    const { container } = render(<ScreenFrame {...props()} />);

    expect(container.querySelector('header')?.textContent).toContain('FlowHub');
    expect(container.querySelector('h1')?.textContent).toBe('搬送の一覧');
    expect(container.querySelector('#content')).not.toBeNull();
  });

  it('メッセージの列は、最初はたたんでおき、タブで開け閉めする', () => {
    const { container } = render(<ScreenFrame {...props()} />);

    expect(container.querySelector('#message-rail')).toBeNull();
    expect(tab(container).textContent).toContain('3');

    click(tab(container));
    expect(container.querySelector('#message-rail')).not.toBeNull();
    click(tab(container));
    expect(container.querySelector('#message-rail')).toBeNull();
  });

  it('開いたメッセージの列は、中身が短くても下の画面が透けないよう、列の全体に背景を塗る', () => {
    const { container } = render(<ScreenFrame {...props({ messages: [] })} />);

    click(tab(container));

    expect(container.querySelector('#message-rail')?.classList.contains('bg-paper')).toBe(true);
  });

  it('開いたメッセージの列は、外を押すと閉じる', () => {
    const { container } = render(<ScreenFrame {...props()} />);

    click(tab(container));
    click(container.querySelector('#content')!);

    expect(container.querySelector('#message-rail')).toBeNull();
  });

  it('メニューで選んだ画面を知らせる', () => {
    const onNavigate = vi.fn();
    const { container } = render(<ScreenFrame {...props({ onNavigate })} />);

    click(container.querySelector('button[aria-controls="main-menu"]')!);
    click(container.querySelector('[data-screen="map"]')!);

    expect(onNavigate).toHaveBeenCalledWith('map');
  });
});
