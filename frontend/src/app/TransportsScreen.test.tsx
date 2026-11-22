import { describe, expect, it, vi } from 'vitest';
import { agvId } from '../map/agv';
import { click, render } from '../test/render';
import { initialRoles } from './permissions';
import type { Transport } from './transports';
import { TransportsScreen } from './TransportsScreen';

const at = (hhmm: string) => new Date(`2026-11-23T${hhmm}:00`);

const base = {
  kind: '届ける',
  from: 'ST-1',
  to: 'DEV-1',
  cargo: '[品種A] 1個',
  requestedAt: at('09:00'),
  stepNote: '',
} as const;

const list: Transport[] = [
  { ...base, number: 14, status: { stage: 'queued' }, neededBy: at('10:20'), agv: null, step: '割り当て待ち' },
  { ...base, number: 12, status: { stage: 'running' }, neededBy: at('10:02'), agv: agvId('AGV-1'), step: '搬送先へ移動' },
  { ...base, number: 11, status: { stage: 'suspended', positionResetDone: false }, neededBy: at('09:40'), agv: agvId('AGV-3'), step: '中断' },
  { ...base, number: 10, status: { stage: 'suspended', positionResetDone: true }, neededBy: at('09:50'), agv: agvId('AGV-2'), step: '中断' },
];

const rows = (c: HTMLElement) => [...c.querySelectorAll('tbody tr')].map((r) => r.querySelector('td')?.textContent);
const tab = (c: HTMLElement, name: string) => [...c.querySelectorAll<HTMLButtonElement>('[role="tab"]')].find((t) => t.textContent?.startsWith(name))!;
const button = (row: Element, name: string) => [...row.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent === name)!;
const rowOf = (c: HTMLElement, number: number) => [...c.querySelectorAll('tbody tr')].find((r) => r.querySelector('td')?.textContent === `#${number}`)!;

function setup(overrides: Partial<Parameters<typeof TransportsScreen>[0]> = {}) {
  const onCancel = vi.fn();
  const onRetry = vi.fn();
  const view = render(<TransportsScreen transports={list} permissions={initialRoles.運用} onCancel={onCancel} onRetry={onRetry} {...overrides} />);
  return { ...view, onCancel, onRetry };
}

describe('TransportsScreen（搬送の一覧）', () => {
  it('タブに件数を出し、中断した搬送があれば「中断した」を開く', () => {
    const { container } = setup();

    expect(tab(container, '待ち行列').textContent).toBe('待ち行列1');
    expect(tab(container, '中断した').getAttribute('aria-selected')).toBe('true');
    expect(rows(container)).toEqual(['#11', '#10']);
  });

  it('タブを切り替える', () => {
    const { container } = setup();

    click(tab(container, '走っている'));

    expect(rows(container)).toEqual(['#12']);
  });

  it('取りやめるは、確かめる画面で「取りやめる」を選んだときだけ送る', () => {
    const { container, onCancel } = setup();

    click(button(rowOf(container, 11), '取りやめる'));
    expect(container.querySelector('[role="alertdialog"]')?.textContent).toContain('#11');
    click(button(container.querySelector('[role="alertdialog"]')!, 'やめる'));
    expect(onCancel).not.toHaveBeenCalled();

    click(button(rowOf(container, 11), '取りやめる'));
    click(button(container.querySelector('[role="alertdialog"]')!, '取りやめる'));
    expect(onCancel).toHaveBeenCalledWith(11);
    expect(container.querySelector('[role="alertdialog"]')).toBeNull();
  });

  it('やり直すは、位置リセットが済むまで押せず、理由を出す', () => {
    const { container, onRetry } = setup();

    expect(button(rowOf(container, 11), 'やり直す').disabled).toBe(true);
    expect(rowOf(container, 11).textContent).toContain('AGV-3 の位置リセットのあとに、やり直せます。');

    click(button(rowOf(container, 10), 'やり直す'));
    click(button(container.querySelector('[role="alertdialog"]')!, 'やり直す'));
    expect(onRetry).toHaveBeenCalledWith(10);
  });

  it('パーミッションがなければ、ボタンを押せず、理由を出す', () => {
    const { container } = setup({ permissions: initialRoles.閲覧のみ });

    expect(button(rowOf(container, 10), '取りやめる').disabled).toBe(true);
    expect(container.textContent).toContain('運用以上のロールで使えます');
  });

  it('タブに搬送がないときは、そのことを出す', () => {
    const { container } = setup({ transports: [] });

    expect(container.textContent).toContain('走っている搬送はありません');
  });
});
