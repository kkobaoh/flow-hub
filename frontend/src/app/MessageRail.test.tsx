import { describe, expect, it, vi } from 'vitest';
import { click, render } from '../test/render';
import { MessageRail, MessageTab, type Message } from './MessageRail';

const messages: Message[] = [
  { id: 'm3', time: '10:42', title: '予約が取れずに止まっている', detail: 'AGV-1 が、パネル P4 へ向かう予約を待っています。', unread: true },
  { id: 'm2', time: '10:31', title: '行ける充電ポートがない', detail: 'AGV-2 は、充電ポートが空いたら向かいます。', unread: true },
  { id: 'm1', time: '09:12', title: '補充を頼んだ', detail: '[品種A] を 2個、上位システムに頼みました。', unread: false },
];

function byText(root: ParentNode, text: string): HTMLButtonElement {
  const found = [...root.querySelectorAll('button')].find((b) => b.textContent === text);
  if (!found) {
    throw new Error(`「${text}」のボタンがありません`);
  }
  return found;
}

const titles = (c: HTMLElement) => [...c.querySelectorAll('[data-message]')].map((e) => e.getAttribute('data-message'));

describe('MessageRail', () => {
  it('最初は、まだ見ていないメッセージだけを、届いた順のまま並べる', () => {
    const { container } = render(<MessageRail messages={messages} onMarkAllSeen={() => {}} onClose={() => {}} />);

    expect(container.querySelector('h2')?.textContent).toBe('メッセージ');
    expect(titles(container)).toEqual(['m3', 'm2']);
  });

  it('「すべて」に切り替えると、見たメッセージも並べ、見たかどうかを印で見分けられるようにする', () => {
    const { container } = render(<MessageRail messages={messages} onMarkAllSeen={() => {}} onClose={() => {}} />);

    click(byText(container, 'すべて'));

    expect(titles(container)).toEqual(['m3', 'm2', 'm1']);
    expect(container.querySelector('[data-message="m1"] [aria-label]')?.getAttribute('aria-label')).toBe('見た');
    expect(container.querySelector('[data-message="m3"] [aria-label]')?.getAttribute('aria-label')).toBe('まだ見ていない');
  });

  it('「すべて見たことにする」で知らせる。まだ見ていないものがなければ押せない', () => {
    const onMarkAllSeen = vi.fn();
    const { container, rerender } = render(<MessageRail messages={messages} onMarkAllSeen={onMarkAllSeen} onClose={() => {}} />);

    click(byText(container, 'すべて見たことにする'));
    expect(onMarkAllSeen).toHaveBeenCalledTimes(1);

    rerender(<MessageRail messages={messages.map((m) => ({ ...m, unread: false }))} onMarkAllSeen={onMarkAllSeen} onClose={() => {}} />);
    expect(byText(container, 'すべて見たことにする').disabled).toBe(true);
  });

  it('並べるものがなければ、そう出す', () => {
    const { container } = render(<MessageRail messages={[]} onMarkAllSeen={() => {}} onClose={() => {}} />);

    expect(container.textContent).toContain('まだ見ていないメッセージはありません');
    click(byText(container, 'すべて'));
    expect(container.textContent).toContain('メッセージはありません');
  });
});

describe('MessageTab', () => {
  it('たたんでいるときは、まだ見ていない件数を出し、押すと開く', () => {
    const onToggle = vi.fn();
    const { container } = render(<MessageTab open={false} unreadCount={3} onToggle={onToggle} />);
    const tab = container.querySelector('button')!;

    expect(tab.getAttribute('aria-expanded')).toBe('false');
    expect(tab.getAttribute('aria-label')).toBe('メッセージの列を開く（まだ見ていないもの 3件）');
    expect(tab.textContent).toContain('3');
    click(tab);
    expect(onToggle).toHaveBeenCalledTimes(1);
  });

  it('開いているときは、たたむためのタブにし、件数は出さない', () => {
    const { container } = render(<MessageTab open unreadCount={3} onToggle={() => {}} />);
    const tab = container.querySelector('button')!;

    expect(tab.getAttribute('aria-expanded')).toBe('true');
    expect(tab.getAttribute('aria-label')).toBe('メッセージの列をたたむ');
    expect(tab.textContent).not.toContain('3');
  });

  it('まだ見ていないものがなければ、件数を出さない', () => {
    const { container } = render(<MessageTab open={false} unreadCount={0} onToggle={() => {}} />);

    expect(container.querySelector('[data-count]')).toBeNull();
  });
});
