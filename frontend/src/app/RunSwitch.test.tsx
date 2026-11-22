import { describe, expect, it, vi } from 'vitest';
import { click, pressKey, render } from '../test/render';
import { RunSwitch } from './RunSwitch';

function buttonByText(root: ParentNode, text: string): HTMLButtonElement {
  const found = [...root.querySelectorAll('button')].find((b) => b.textContent === text);
  if (!found) {
    throw new Error(`「${text}」のボタンがありません`);
  }
  return found;
}

const dialog = (c: HTMLElement) => c.querySelector<HTMLElement>('[role="alertdialog"]');

describe('RunSwitch', () => {
  it('今の運転状態の側のボタンを、押されている状態にする', () => {
    const { container, rerender } = render(<RunSwitch running onStart={() => {}} onStop={() => {}} />);

    expect(buttonByText(container, 'スタート').getAttribute('aria-pressed')).toBe('true');
    expect(buttonByText(container, '停止').getAttribute('aria-pressed')).toBe('false');

    rerender(<RunSwitch running={false} onStart={() => {}} onStop={() => {}} />);
    expect(buttonByText(container, 'スタート').getAttribute('aria-pressed')).toBe('false');
    expect(buttonByText(container, '停止').getAttribute('aria-pressed')).toBe('true');
  });

  it('運転中に「停止」を押すと、確かめる画面を出し、「停止する」で停止する', () => {
    const onStop = vi.fn();
    const { container } = render(<RunSwitch running onStart={() => {}} onStop={onStop} />);

    click(buttonByText(container, '停止'));
    const d = dialog(container);
    expect(d?.textContent).toContain('運転を停止しますか');
    expect(d?.textContent).toContain('一時停止');
    expect(onStop).not.toHaveBeenCalled();

    click(buttonByText(d!, '停止する'));
    expect(onStop).toHaveBeenCalledTimes(1);
    expect(dialog(container)).toBeNull();
  });

  it('停止中に「スタート」を押すと、確かめる画面を出し、「スタートする」でスタートする', () => {
    const onStart = vi.fn();
    const { container } = render(<RunSwitch running={false} onStart={onStart} onStop={() => {}} />);

    click(buttonByText(container, 'スタート'));
    expect(dialog(container)?.textContent).toContain('運転をスタートしますか');

    click(buttonByText(dialog(container)!, 'スタートする'));
    expect(onStart).toHaveBeenCalledTimes(1);
  });

  it('「やめる」か Esc で、何もせずに閉じる', () => {
    const onStop = vi.fn();
    const { container } = render(<RunSwitch running onStart={() => {}} onStop={onStop} />);

    click(buttonByText(container, '停止'));
    click(buttonByText(dialog(container)!, 'やめる'));
    expect(dialog(container)).toBeNull();

    click(buttonByText(container, '停止'));
    pressKey(dialog(container)!, 'Escape');
    expect(dialog(container)).toBeNull();

    expect(onStop).not.toHaveBeenCalled();
  });

  it('今と同じ側を押しても、何もしない', () => {
    const { container } = render(<RunSwitch running onStart={() => {}} onStop={() => {}} />);

    click(buttonByText(container, 'スタート'));

    expect(dialog(container)).toBeNull();
  });

  it('確かめる画面を開いたら、「やめる」に焦点を置く（押しまちがえて実行しないように）', () => {
    const { container } = render(<RunSwitch running onStart={() => {}} onStop={() => {}} />);

    click(buttonByText(container, '停止'));

    expect(document.activeElement?.textContent).toBe('やめる');
  });
});
