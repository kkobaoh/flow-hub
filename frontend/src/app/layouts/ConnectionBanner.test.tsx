import { describe, expect, it } from 'vitest';
import { render } from '../../testing/render';
import { ConnectionBanner } from './ConnectionBanner';

describe('ConnectionBanner', () => {
  it('つながっているときは、何も出さない', () => {
    const { container } = render(<ConnectionBanner connection="connected" />);

    expect(container.textContent).toBe('');
  });

  it('初めてつないでいるあいだは、つないでいることと、操作できないことを知らせる', () => {
    const { container } = render(<ConnectionBanner connection="connecting" />);

    const banner = container.querySelector('[role="status"]');
    expect(banner?.textContent).toContain('サーバーとつないでいます');
    expect(banner?.textContent).toContain('操作はできません');
  });

  it('切れたときは、すぐ気づけるように知らせ、表示が切れる前の様子だと伝える', () => {
    const { container } = render(<ConnectionBanner connection="reconnecting" />);

    const banner = container.querySelector('[role="alert"]');
    expect(banner?.textContent).toContain('サーバーとつながっていません（つなぎ直しています）');
    expect(banner?.textContent).toContain('切れる前の様子');
    expect(banner?.textContent).toContain('操作はできません');
  });
});
