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

  it('URL で指定した装置を、装置とストッカーの画面で開く', () => {
    window.location.hash = '#/devices/DEV-2';
    const { container } = render(<App />);

    expect(container.querySelector<HTMLSelectElement>('select[aria-label="装置"]')?.value).toBe('DEV-2');
    window.location.hash = '';
  });

  it('マップの編集で保存していない変更があれば、ほかの画面へ移る前に確かめる', () => {
    window.location.hash = '#/mapEdit';
    const { container } = render(<App />);

    click(container.querySelector('[aria-label="置くもの"] button[data-tool="Normal"]')!);
    click(container.querySelector('[data-cell]')!);
    click(container.querySelector('button[aria-controls="main-menu"]')!);
    click(container.querySelector('[data-screen="transports"]')!);

    const dialog = container.querySelector('[role="alertdialog"]')!;
    expect(dialog.textContent).toContain('保存していない変更を捨てて移りますか？');
    click([...dialog.querySelectorAll('button')].find((b) => b.textContent === '変更を捨てて移る')!);
    expect(container.querySelector('h1')?.textContent).toBe('搬送の一覧');
    window.location.hash = '';
  });

  it('サーバーとつながるまでは、見本のデータで動かしていることを知らせる', () => {
    const { container } = render(<App />);

    expect(container.textContent).toContain('見本のデータで表示しています');
  });
});
