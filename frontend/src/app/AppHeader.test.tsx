import { describe, expect, it, vi } from 'vitest';
import { click, pressKey, render } from '../test/render';
import { AppHeader, type AppHeaderProps } from './AppHeader';

const base: AppHeaderProps = {
  run: 'running',
  user: { name: '[ユーザー名]', role: '運用' },
  current: 'map',
  mapVersion: 'マップ 版3（公開済み）',
  onNavigate: () => {},
  onChangePassword: () => {},
  onLogout: () => {},
};

function q<T extends Element = HTMLElement>(root: ParentNode, selector: string): T {
  const found = root.querySelector<T>(selector);
  if (!found) {
    throw new Error(`${selector} がありません`);
  }
  return found;
}

const menuButton = (c: HTMLElement) => q<HTMLButtonElement>(c, 'button[aria-controls="main-menu"]');

describe('AppHeader', () => {
  it('システムの名前と、運転状態を出す', () => {
    const { container, rerender } = render(<AppHeader {...base} />);

    expect(container.textContent).toContain('FlowHub');
    expect(q(container, '[role="status"]').textContent).toBe('運転中');

    rerender(<AppHeader {...base} run="stopped" />);
    expect(q(container, '[role="status"]').textContent).toBe('停止中');

    rerender(<AppHeader {...base} run="unknown" />);
    expect(q(container, '[role="status"]').textContent).toBe('運転状態を確かめています'); // サーバーから届くまでは、停止中とも運転中とも出さない
  });

  it('名前を押すと、マップの画面へ移る', () => {
    const onNavigate = vi.fn();
    const { container } = render(<AppHeader {...base} current="agvs" onNavigate={onNavigate} />);

    click(q<HTMLButtonElement>(container, 'button[data-home]'));

    expect(onNavigate).toHaveBeenCalledWith('map');
  });

  it('メニューは閉じていて、押すと開く', () => {
    const { container } = render(<AppHeader {...base} />);

    expect(menuButton(container).getAttribute('aria-expanded')).toBe('false');
    expect(container.querySelector('#main-menu')).toBeNull();

    click(menuButton(container));

    expect(menuButton(container).getAttribute('aria-expanded')).toBe('true');
    expect(container.querySelector('#main-menu')).not.toBeNull();
  });

  it('メニューには、すべての画面を出し、今いる画面に印を付ける。構成を変える画面は「設定」の下に出す', () => {
    const { container } = render(<AppHeader {...base} current="transports" />);
    click(menuButton(container));
    const nav = q(container, '#main-menu');

    const items = [...nav.querySelectorAll('[data-screen]')].map((e) => e.getAttribute('data-screen'));
    expect(items).toEqual(['map', 'transports', 'devices', 'station', 'consumables', 'mapEdit', 'agvs', 'consumableConfig', 'users']);
    expect(q(nav, '[aria-current="page"]').getAttribute('data-screen')).toBe('transports');
    const settingHeading = [...nav.querySelectorAll('*')].findIndex((e) => e.textContent === '設定');
    const mapEdit = [...nav.querySelectorAll('*')].findIndex((e) => e.getAttribute('data-screen') === 'mapEdit');
    expect(settingHeading).toBeGreaterThan(-1);
    expect(settingHeading).toBeLessThan(mapEdit);
  });

  it('画面を選ぶと知らせて、メニューを閉じる', () => {
    const onNavigate = vi.fn();
    const { container } = render(<AppHeader {...base} onNavigate={onNavigate} />);
    click(menuButton(container));

    click(q(container, '[data-screen="devices"]'));

    expect(onNavigate).toHaveBeenCalledWith('devices');
    expect(container.querySelector('#main-menu')).toBeNull();
  });

  it('メニューの一番上に、ユーザー名とロール、パスワードの変更とログアウトを出す。一番下にマップの版を出す', () => {
    const onChangePassword = vi.fn();
    const onLogout = vi.fn();
    const { container } = render(<AppHeader {...base} onChangePassword={onChangePassword} onLogout={onLogout} />);
    click(menuButton(container));
    const nav = q(container, '#main-menu');

    expect(nav.textContent).toContain('[ユーザー名]');
    expect(nav.textContent).toContain('運用');
    expect(nav.textContent).toContain('マップ 版3（公開済み）');
    const buttons = [...nav.querySelectorAll('button')];
    click(buttons.find((b) => b.textContent === 'パスワードを変える')!);
    click(menuButton(container));
    click([...q(container, '#main-menu').querySelectorAll('button')].find((b) => b.textContent === 'ログアウト')!);

    expect(onChangePassword).toHaveBeenCalledTimes(1);
    expect(onLogout).toHaveBeenCalledTimes(1);
  });

  it('メニューの外を押すか、Esc を押すと閉じる', () => {
    const { container } = render(
      <div>
        <AppHeader {...base} />
        <p id="outside">外</p>
      </div>,
    );

    click(menuButton(container));
    click(q(container, '#outside'));
    expect(container.querySelector('#main-menu')).toBeNull();

    click(menuButton(container));
    pressKey(q(container, '#main-menu'), 'Escape');
    expect(container.querySelector('#main-menu')).toBeNull();
  });
});
