import { describe, expect, it } from 'vitest';
import { render } from './test/render';
import { App } from './App';

describe('App', () => {
  it('入り口は、全体の様子（マップ）の画面にする', () => {
    const { container } = render(<App />);

    expect(container.querySelector('header')?.textContent).toContain('FlowHub');
    expect(container.querySelector('svg[aria-label="マップ"]')).not.toBeNull();
  });

  it('サーバーとつながるまでは、見本のデータで動かしていることを知らせる', () => {
    const { container } = render(<App />);

    expect(container.textContent).toContain('見本のデータで表示しています');
  });
});
