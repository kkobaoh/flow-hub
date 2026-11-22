import { act } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { heading, panelId } from '../map/model';
import { change, click, render } from '../test/render';
import type { ManualInput, ManualOutcome } from './manual';
import { ManualPanel, type ManualPanelProps } from './ManualPanel';

const idle: ManualInput = { operation: 'idle', activity: 'none', heading: heading(90) };
const accepted = () => Promise.resolve<ManualOutcome>('accepted');

function props(overrides: Partial<ManualPanelProps> = {}): ManualPanelProps {
  return {
    input: idle,
    destinations: [
      { id: panelId('SB'), label: 'SB（待機パネル）' },
      { id: panelId('P1'), label: 'P1' },
    ],
    onMove: accepted,
    onStep: accepted,
    onCarrier: accepted,
    ...overrides,
  };
}

function byText(root: ParentNode, text: string): HTMLButtonElement {
  const found = [...root.querySelectorAll('button')].find((b) => b.textContent === text);
  if (!found) {
    throw new Error(`「${text}」のボタンがありません`);
  }
  return found;
}

function field<T extends HTMLElement>(root: ParentNode, label: string): T {
  const l = [...root.querySelectorAll('label')].find((e) => e.textContent?.startsWith(label));
  const id = l?.getAttribute('for');
  const found = id ? root.querySelector<T>(`#${CSS.escape(id)}`) : null;
  if (!found) {
    throw new Error(`「${label}」の欄がありません`);
  }
  return found;
}

describe('ManualPanel', () => {
  it('行き先のパネルと、着いたときの向きを選んで送る', async () => {
    const onMove = vi.fn(accepted);
    const { container } = render(<ManualPanel {...props({ onMove })} />);

    change(field<HTMLSelectElement>(container, '行き先のパネル'), 'P1');
    change(field<HTMLSelectElement>(container, '着いたときの向き'), '180');
    await act(async () => byText(container, 'この行き先へ動かす').click());

    expect(onMove).toHaveBeenCalledWith('P1', 180);
  });

  it('着いたときの向きを指定しなければ、向きなしで送る', async () => {
    const onMove = vi.fn(accepted);
    const { container } = render(<ManualPanel {...props({ onMove })} />);

    await act(async () => byText(container, 'この行き先へ動かす').click());

    expect(onMove).toHaveBeenCalledWith('SB', null);
  });

  it('送ってから結果が返るまでは、送るボタンを押せない。受け付けなかったときは理由を出す', async () => {
    let finish: (o: ManualOutcome) => void = () => {};
    const onMove = vi.fn(() => new Promise<ManualOutcome>((resolve) => (finish = resolve)));
    const { container } = render(<ManualPanel {...props({ onMove })} />);

    click(byText(container, 'この行き先へ動かす'));
    expect(byText(container, 'この行き先へ動かす').disabled).toBe(true);
    click(byText(container, 'この行き先へ動かす'));
    expect(onMove).toHaveBeenCalledTimes(1);

    await act(async () => finish('blocked'));

    expect(byText(container, 'この行き先へ動かす').disabled).toBe(false);
    expect(container.querySelector('[role="status"]')?.textContent).toBe('ほかの AGV に塞がれていて、今は行けません。');
  });

  it('1つずつ動かす：前進、後退、回転、角度を指定した回転を送る', async () => {
    const onStep = vi.fn(accepted);
    const { container } = render(<ManualPanel {...props({ onStep })} />);
    click(byText(container, '1つずつ動かす'));

    await act(async () => byText(container, '前進').click());
    await act(async () => byText(container, '後退').click());
    await act(async () => byText(container, '180°（南）').click());
    change(field<HTMLInputElement>(container, '角度を指定した回転'), '45');
    await act(async () => byText(container, 'この角度に回す').click());

    expect(onStep.mock.calls).toEqual([
      [{ kind: 'forward' }],
      [{ kind: 'backward' }],
      [{ kind: 'rotate', heading: 180 }],
      [{ kind: 'freeRotate', heading: 45 }],
    ]);
  });

  it('角度を指定した回転は、0〜359 の整数だけ送れる', () => {
    const { container } = render(<ManualPanel {...props()} />);
    click(byText(container, '1つずつ動かす'));

    change(field<HTMLInputElement>(container, '角度を指定した回転'), '400');

    expect(byText(container, 'この角度に回す').disabled).toBe(true);
    expect(container.textContent).toContain('0〜359 の整数を入れてください。');
  });

  it('ラインの向きを向いていないときは、前進と後退を押せず、理由を出す', () => {
    const { container } = render(<ManualPanel {...props({ input: { ...idle, heading: heading(45) } })} />);
    click(byText(container, '1つずつ動かす'));

    expect(byText(container, '前進').disabled).toBe(true);
    expect(container.textContent).toContain('先に「回転」でラインの向きに合わせてください。');
    expect(byText(container, '0°（北）').disabled).toBe(false);
  });

  it('緊急停止中は、何も送れず、理由を出す', () => {
    const { container } = render(<ManualPanel {...props({ input: { ...idle, operation: 'emergencyStopped' } })} />);

    expect(byText(container, 'この行き先へ動かす').disabled).toBe(true);
    expect(byText(container, 'キャリアを上げる').disabled).toBe(true);
    expect(container.textContent).toContain('緊急停止中は送れません。');
  });

  it('キャリアを上げ下げする', async () => {
    const onCarrier = vi.fn(accepted);
    const { container } = render(<ManualPanel {...props({ onCarrier })} />);

    await act(async () => byText(container, 'キャリアを上げる').click());
    await act(async () => byText(container, 'キャリアを下げる').click());

    expect(onCarrier.mock.calls).toEqual([['raise'], ['lower']]);
  });
});
