import { act } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { agvId } from '../features/fleet/agv';
import { click, render } from '../testing/render';
import { initialRoles } from '../features/auth/permissions';
import type { Transport } from '../features/dispatch/transports';
import { DispatchPage, type DispatchPageProps } from './DispatchPage';

const at = (hhmm: string) => new Date(`2026-11-23T${hhmm}:00`);

const base = {
  kind: '届ける',
  from: 'ST-1',
  to: 'DEV-1',
  cargo: '[品種A] 1個',
  stepNote: '',
} as const;

const list: Transport[] = [
  { ...base, number: 14, status: { stage: 'queued' }, neededBy: at('10:20'), agv: null, step: '割り当て待ち' },
  { ...base, number: 12, status: { stage: 'running' }, neededBy: at('10:02'), agv: agvId('AGV-1'), step: '搬送先へ移動' },
  { ...base, number: 11, status: { stage: 'suspended' }, neededBy: at('09:40'), agv: agvId('AGV-3'), step: '中断' },
  { ...base, number: 10, status: { stage: 'suspended' }, neededBy: at('09:50'), agv: agvId('AGV-2'), step: '中断' },
];

const rows = (c: HTMLElement) => [...c.querySelectorAll('tbody tr')].map((r) => r.querySelector('td')?.textContent);
const tab = (c: HTMLElement, name: string) => [...c.querySelectorAll<HTMLButtonElement>('[role="tab"]')].find((t) => t.textContent?.startsWith(name))!;
const button = (row: Element, name: string) => [...row.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent === name)!;
const rowOf = (c: HTMLElement, number: number) => [...c.querySelectorAll('tbody tr')].find((r) => r.querySelector('td')?.textContent === `#${number}`)!;

function setup(overrides: Partial<DispatchPageProps> = {}) {
  const onCancel = vi.fn<DispatchPageProps['onCancel']>().mockResolvedValue(null);
  const view = render(<DispatchPage transports={list} permissions={initialRoles.運用} offline={false} onCancel={onCancel} {...overrides} />);
  return { ...view, onCancel };
}

/** 取りやめるを押し、確かめる画面で「取りやめる」を選ぶ。 */
async function cancel(c: HTMLElement, number: number) {
  click(button(rowOf(c, number), '取りやめる'));
  await act(async () => {
    click(button(c.querySelector('[role="alertdialog"]')!, '取りやめる'));
    await Promise.resolve();
  });
}

describe('DispatchPage（搬送の一覧）', () => {
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

  it('取りやめるは、確かめる画面で「取りやめる」を選んだときだけ送る', async () => {
    const { container, onCancel } = setup();
    click(tab(container, '走っている'));

    click(button(rowOf(container, 12), '取りやめる'));
    expect(container.querySelector('[role="alertdialog"]')?.textContent).toContain('#12');
    click(button(container.querySelector('[role="alertdialog"]')!, 'やめる'));
    expect(onCancel).not.toHaveBeenCalled();

    await cancel(container, 12);
    expect(onCancel).toHaveBeenCalledWith(12);
    expect(container.querySelector('[role="alertdialog"]')).toBeNull();
  });

  it('取りやめの結果の知らせがあれば、一覧の上に出す', async () => {
    const onCancel = vi.fn<DispatchPageProps['onCancel']>().mockResolvedValue('取りやめを受け付けました。');
    const { container } = setup({ onCancel });
    click(tab(container, '待ち行列'));

    await cancel(container, 14);

    expect(container.querySelector('[role="status"]')?.textContent).toBe('取りやめを受け付けました。');
  });

  it('中断した搬送は、やり直すも取りやめるもまだ押せず、理由を出す', () => {
    const { container } = setup();

    expect(button(rowOf(container, 11), 'やり直す').disabled).toBe(true);
    expect(button(rowOf(container, 11), '取りやめる').disabled).toBe(true);
    expect(rowOf(container, 11).textContent).toContain('中断した搬送のやり直しと取りやめは、まだできません');
  });

  it('サーバーとつながっていないときは、取りやめを押せない', () => {
    const { container } = setup({ offline: true });
    click(tab(container, '走っている'));

    expect(button(rowOf(container, 12), '取りやめる').disabled).toBe(true);
    expect(rowOf(container, 12).textContent).toContain('サーバーとつながっていないので、取りやめられません。');
  });

  it('パーミッションがなければ、ボタンを押せず、理由を出す', () => {
    const { container } = setup({ permissions: initialRoles.閲覧のみ });
    click(tab(container, '走っている'));

    expect(button(rowOf(container, 12), '取りやめる').disabled).toBe(true);
    expect(container.textContent).toContain('運用以上のロールで使えます');
  });

  it('タブに搬送がないときは、そのことを出す', () => {
    const { container } = setup({ transports: [] });

    expect(container.textContent).toContain('走っている搬送はありません');
  });
});
