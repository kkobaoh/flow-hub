import { describe, expect, it, vi } from 'vitest';
import { agvId } from '../map/agv';
import { heading, panelId } from '../map/model';
import { act } from 'react';
import { click, render } from '../test/render';
import { AgvCard, type AgvCardProps, type AgvDetail } from './AgvCard';
import { initialRoles } from './permissions';

const agv: AgvDetail = {
  id: agvId('AGV-1'),
  label: '1',
  position: { kind: 'onMap', panel: panelId('P3'), heading: heading(90) },
  status: { activity: 'idle', charging: 'notCharging', carrierMoving: false },
  control: 'auto',
  operation: 'idle',
  transport: null,
  batteryPercent: 72,
};

function props(overrides: Partial<AgvCardProps> = {}): AgvCardProps {
  return {
    agv,
    onChargingPin: false,
    permissions: initialRoles.保守,
    onClose: () => {},
    onEmergencyStop: () => {},
    onChangeControl: () => {},
    onAlarmClear: () => {},
    onPositionReset: () => {},
    panels: [
      { id: panelId('SB'), label: 'SB（待機パネル）' },
      { id: panelId('P3'), label: 'P3' },
    ],
    offline: false,
    sending: false,
    notice: null,
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

const dialog = (c: HTMLElement) => c.querySelector<HTMLElement>('[role="alertdialog"]');

function choose(el: HTMLSelectElement, value: string) {
  act(() => {
    el.value = value;
    el.dispatchEvent(new Event('change', { bubbles: true }));
  });
}

describe('AgvCard', () => {
  it('見出しに AGV の名前を出し、閉じるボタンで閉じる', () => {
    const onClose = vi.fn();
    const { container } = render(<AgvCard {...props({ onClose })} />);

    expect(container.querySelector('h2')?.textContent).toBe('AGV-1');
    click(container.querySelector('button[aria-label="閉じる"]')!);
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('状態、コントロールステータス、位置、搬送、充電残量を出す', () => {
    const { container } = render(<AgvCard {...props({ agv: { ...agv, transport: { label: '#12 ST-1 から DEV-1 へ', step: '搬送先へ移動' } } })} />);
    const text = container.textContent ?? '';

    expect(text).toContain('待機中');
    expect(text).toContain('オート');
    expect(text).toContain('P3');
    expect(text).toContain('90°（東）');
    expect(text).toContain('#12 ST-1 から DEV-1 へ');
    expect(text).toContain('搬送先へ移動');
    expect(text).toContain('72%');
  });

  it('充電残量がまだ届いていなければ「不明」、搬送がなければ「なし」と出す', () => {
    const { container } = render(<AgvCard {...props({ agv: { ...agv, batteryPercent: null } })} />);

    expect(container.textContent).toContain('不明');
    expect(container.textContent).toContain('なし');
  });

  it('充電パネルで充電しているときは、状態を「充電中」と出す', () => {
    const { container } = render(
      <AgvCard {...props({ onChargingPin: true, agv: { ...agv, status: { ...agv.status, charging: 'charging' } } })} />,
    );

    expect(container.textContent).toContain('充電中');
  });

  it('緊急停止を押すと、その AGV に緊急停止を送る', () => {
    const onEmergencyStop = vi.fn();
    const { container } = render(<AgvCard {...props({ onEmergencyStop })} />);

    click(byText(container, '緊急停止'));

    expect(onEmergencyStop).toHaveBeenCalledWith('AGV-1');
  });

  it('切り離し中は、どのコマンドも送らない', () => {
    const { container } = render(<AgvCard {...props({ agv: { ...agv, control: 'detached' } })} />);

    expect(byText(container, '緊急停止').disabled).toBe(true);
    expect(byText(container, 'アラームクリア').disabled).toBe(true);
    expect(byText(container, '位置リセット').disabled).toBe(true);
    expect(container.textContent).toContain('切り離し中の AGV には、コマンドを送りません。');
  });

  it('コントロールステータスを選ぶと知らせる。今のものは押された状態、選べないものは理由を出す', () => {
    const onChangeControl = vi.fn();
    const { container } = render(<AgvCard {...props({ onChangeControl, agv: { ...agv, operation: 'traveling' } })} />);

    expect(byText(container, 'オート').getAttribute('aria-pressed')).toBe('true');
    expect(byText(container, 'マニュアル').disabled).toBe(true);
    expect(container.textContent).toContain('走っているあいだは切り替えられません。');

    click(byText(container, '割り当て停止'));
    expect(onChangeControl).toHaveBeenCalledWith('AGV-1', 'assignmentPaused', null); // オートと割り当て停止の間は、確かめずに送る
    expect(dialog(container)).toBeNull();
  });

  it('保守の操作になる切り替えは、確かめる画面を出してから送る', () => {
    const onChangeControl = vi.fn();
    const { container } = render(<AgvCard {...props({ onChangeControl })} />);

    click(byText(container, 'マニュアル'));
    expect(onChangeControl).not.toHaveBeenCalled();
    expect(dialog(container)?.textContent).toContain('AGV-1 を「マニュアル」に切り替えますか');

    click(byText(dialog(container)!, 'マニュアルに切り替える'));
    expect(onChangeControl).toHaveBeenCalledWith('AGV-1', 'manual', null);
    expect(dialog(container)).toBeNull();
  });

  it('切り離しから戻すときは、位置を選ぶ画面を出し、選んだ位置を付けて送る', () => {
    const onChangeControl = vi.fn();
    const { container } = render(<AgvCard {...props({ onChangeControl, agv: { ...agv, control: 'detached', position: { kind: 'unknown' }, operation: 'positionUnknown' } })} />);

    click(byText(container, 'オート'));
    const d = dialog(container)!;
    expect(d.textContent).toContain('AGV-1 を「オート」に戻しますか');
    choose(d.querySelector<HTMLSelectElement>('select[aria-label="パネル"]')!, 'SB');
    expect(byText(d, 'オートに戻す').disabled).toBe(true); // 位置がわからないので、向きも選ぶまで戻せない
    choose(d.querySelector<HTMLSelectElement>('select[aria-label="向き"]')!, '90');
    click(byText(d, 'オートに戻す'));

    expect(onChangeControl).toHaveBeenCalledWith('AGV-1', 'auto', { panel: 'SB', heading: 90 });
  });

  it('アラームクリアは確かめてから送り、位置リセットは位置を選んでから送る（今の位置を最初から選んでおく）', () => {
    const onAlarmClear = vi.fn();
    const onPositionReset = vi.fn();
    const { container } = render(<AgvCard {...props({ onAlarmClear, onPositionReset })} />);

    click(byText(container, 'アラームクリア'));
    expect(dialog(container)?.textContent).toContain('続きを走り出します');
    click(byText(dialog(container)!, 'アラームクリアする'));
    expect(onAlarmClear).toHaveBeenCalledWith('AGV-1');

    click(byText(container, '位置リセット'));
    click(byText(dialog(container)!, '位置リセットする'));
    expect(onPositionReset).toHaveBeenCalledWith('AGV-1', { panel: 'P3', heading: 90 });
  });

  it('送っている途中は、緊急停止のほかの操作を押せない。緊急停止は押せる', () => {
    const { container } = render(<AgvCard {...props({ sending: true })} />);

    expect(byText(container, '緊急停止').disabled).toBe(false);
    expect(byText(container, '割り当て停止').disabled).toBe(true);
    expect(byText(container, 'アラームクリア').disabled).toBe(true);
    expect(byText(container, '位置リセット').disabled).toBe(true);
  });

  it('サーバーとつながっていないときは、緊急停止も含めて押せない', () => {
    const { container } = render(<AgvCard {...props({ offline: true })} />);

    expect(byText(container, '緊急停止').disabled).toBe(true);
    expect(byText(container, '割り当て停止').disabled).toBe(true);
    expect(byText(container, 'アラームクリア').disabled).toBe(true);
  });

  it('操作が受け付けられなかったときの知らせを、カードの中に出す', () => {
    const { container } = render(<AgvCard {...props({ notice: 'そのパネルは、ほかの AGV が予約しています。' })} />);

    expect(container.querySelector('[role="status"][data-agv-notice]')?.textContent).toBe('そのパネルは、ほかの AGV が予約しています。');
  });

  it('ロック中は、アラームクリアを送らない（位置リセットは送れる）', () => {
    const { container } = render(<AgvCard {...props({ agv: { ...agv, control: 'locked' } })} />);

    expect(byText(container, 'アラームクリア').disabled).toBe(true);
    expect(byText(container, '位置リセット').disabled).toBe(false);
  });

  it('運用のロールには、復旧のボタンを出さず、1行の注記にする', () => {
    const { container } = render(<AgvCard {...props({ permissions: initialRoles.運用 })} />);

    expect([...container.querySelectorAll('button')].some((b) => b.textContent === 'アラームクリア')).toBe(false);
    expect(container.textContent).toContain('アラームクリア、位置リセット、マニュアル操作は、保守以上のロールで使えます。');
  });

  it('保守のロールで、AGV がマニュアルのときだけ、マニュアル操作の欄を出す', () => {
    const manual = <div data-testid="manual">マニュアル操作の欄</div>;

    const auto = render(<AgvCard {...props({ manual })} />);
    expect(auto.container.querySelector('[data-testid="manual"]')).toBeNull();
    expect(auto.container.textContent).toContain('マニュアルに切り替えると、ここから行き先や1つずつの動作を送れます。');

    const manualAgv = render(<AgvCard {...props({ manual, agv: { ...agv, control: 'manual' } })} />);
    expect(manualAgv.container.querySelector('[data-testid="manual"]')).not.toBeNull();

    const operator = render(<AgvCard {...props({ manual, permissions: initialRoles.運用, agv: { ...agv, control: 'manual' } })} />);
    expect(operator.container.querySelector('[data-testid="manual"]')).toBeNull();
  });

  it('閲覧のみのロールは、緊急停止を押せない', () => {
    const { container } = render(<AgvCard {...props({ permissions: initialRoles.閲覧のみ })} />);

    expect(byText(container, '緊急停止').disabled).toBe(true);
  });
});
