import { describe, expect, it } from 'vitest';
import { click, render } from './test/render';
import { App } from './App';

describe('App', () => {
  it('入り口は、全体の様子（マップ）の画面にする', () => {
    const { container } = render(<App />);

    expect(container.querySelector('header')?.textContent).toContain('FlowHub');
    expect(container.querySelector('svg[aria-label="マップ"]')).not.toBeNull();
  });

  it('メニューで選んだ画面に切り替え、URL の # のあとに覚える', () => {
    window.location.hash = '';
    const { container } = render(<App />);

    click(container.querySelector('button[aria-controls="main-menu"]')!);
    click(container.querySelector('[data-screen="transports"]')!);

    expect(container.querySelector('h1')?.textContent).toBe('搬送の一覧');
    expect(container.querySelector('[role="tablist"][aria-label="搬送の段階"]')).not.toBeNull();
    expect(window.location.hash).toBe('#/transports');

    click(container.querySelector('button[aria-controls="main-menu"]')!);
    click(container.querySelector('[data-screen="map"]')!);
    expect(container.querySelector('svg[aria-label="マップ"]')).not.toBeNull();
  });

  it('サーバーとつながるまでは、見本のデータで動かしていることを知らせる', () => {
    const { container } = render(<App />);

    expect(container.textContent).toContain('見本のデータで表示しています');
  });
});
