import { act } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { heading, panelId } from '../map/model';
import { click, pressKey, render } from '../test/render';
import { PositionDialog, type PositionDialogProps } from './PositionDialog';

const panels = [
  { id: panelId('SB'), label: 'SB（待機パネル）' },
  { id: panelId('P3'), label: 'P3' },
];

function props(overrides: Partial<PositionDialogProps> = {}): PositionDialogProps {
  return {
    title: 'AGV-1 を位置リセットしますか',
    confirmLabel: '位置リセットする',
    panels,
    initial: null,
    onConfirm: () => {},
    onCancel: () => {},
    ...overrides,
  };
}

const dialog = (c: HTMLElement) => c.querySelector<HTMLElement>('[role="alertdialog"]')!;
const select = (c: HTMLElement, label: string) => c.querySelector<HTMLSelectElement>(`select[aria-label="${label}"]`)!;
const byText = (root: ParentNode, text: string) => [...root.querySelectorAll('button')].find((b) => b.textContent === text)!;

function choose(el: HTMLSelectElement, value: string) {
  act(() => {
    el.value = value;
    el.dispatchEvent(new Event('change', { bubbles: true }));
  });
}

describe('PositionDialog', () => {
  it('十字の上まで動かしてから行うことを伝え、パネルと 0・90・180・270 度の向きを選ばせる', () => {
    const { container } = render(<PositionDialog {...props()} />);

    expect(dialog(container).textContent).toContain('十字の上');
    expect([...select(container, 'パネル').options].map((o) => o.value)).toEqual(['', 'SB', 'P3']);
    expect([...select(container, '向き').options].map((o) => o.value)).toEqual(['0', '90', '180', '270']);
  });

  it('パネルを選ぶまでは決められない', () => {
    const onConfirm = vi.fn();
    const { container } = render(<PositionDialog {...props({ onConfirm })} />);

    expect(byText(container, '位置リセットする').disabled).toBe(true);
    choose(select(container, 'パネル'), 'P3');
    choose(select(container, '向き'), '180');
    click(byText(container, '位置リセットする'));

    expect(onConfirm).toHaveBeenCalledWith({ panel: 'P3', heading: 180 });
  });

  it('今わかっている位置を、最初から選んでおく（向きが 0・90・180・270 度でなければ、向きは 0 度）', () => {
    const { container, rerender } = render(<PositionDialog {...props({ initial: { panel: panelId('SB'), heading: heading(270) } })} />);
    expect(select(container, 'パネル').value).toBe('SB');
    expect(select(container, '向き').value).toBe('270');

    rerender(<PositionDialog key="other" {...props({ initial: { panel: panelId('P3'), heading: heading(45) } })} />);
    expect(select(container, '向き').value).toBe('0');
  });

  it('「やめる」か Esc で、何もせずに閉じる', () => {
    const onCancel = vi.fn();
    const { container } = render(<PositionDialog {...props({ onCancel })} />);

    click(byText(container, 'やめる'));
    pressKey(dialog(container), 'Escape');

    expect(onCancel).toHaveBeenCalledTimes(2);
  });
});
