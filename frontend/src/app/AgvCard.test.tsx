import { describe, expect, it, vi } from 'vitest';
import { agvId } from '../map/agv';
import { heading, panelId } from '../map/model';
import { click, render } from '../test/render';
import { AgvCard, type AgvCardProps, type AgvDetail } from './AgvCard';
import { initialRoles } from './permissions';

const agv: AgvDetail = {
  id: agvId('AGV-1'),
  label: '1',
  panel: panelId('P3'),
  heading: heading(90),
  status: { activity: 'idle', charging: false, carrierMoving: false },
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

describe('AgvCard', () => {
  it('見出しに AGV の名前を出し、閉じるボタンで閉じる', () => {
    const onClose = vi.fn();
    const { container } = render(<AgvCard {...props({ onClose })} />);

    expect(container.querySelector('h2')?.textContent).toBe('AGV-1');
    click(container.querySelector('button[aria-label="閉じる"]')!);
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('状態、コントロールステータス、位置、搬送、充電残量を出す', () => {
    const { container } = render(<AgvCard {...props({ agv: { ...agv, transport: '#12 ST-1 から DEV-1 へ' } })} />);
    const text = container.textContent ?? '';

    expect(text).toContain('待機中');
    expect(text).toContain('オート');
    expect(text).toContain('P3');
    expect(text).toContain('90°（東）');
    expect(text).toContain('#12 ST-1 から DEV-1 へ');
    expect(text).toContain('72%');
  });

  it('充電残量がまだ届いていなければ「不明」、搬送がなければ「なし」と出す', () => {
    const { container } = render(<AgvCard {...props({ agv: { ...agv, batteryPercent: null } })} />);

    expect(container.textContent).toContain('不明');
    expect(container.textContent).toContain('なし');
  });

  it('充電パネルで充電しているときは、状態を「充電中」と出す', () => {
    const { container } = render(
      <AgvCard {...props({ onChargingPin: true, agv: { ...agv, status: { ...agv.status, charging: true } } })} />,
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
    expect(onChangeControl).toHaveBeenCalledWith('AGV-1', 'assignmentPaused', false);
  });

  it('保守は、アラームクリアと位置リセットを使える', () => {
    const onAlarmClear = vi.fn();
    const onPositionReset = vi.fn();
    const { container } = render(<AgvCard {...props({ onAlarmClear, onPositionReset })} />);

    click(byText(container, 'アラームクリア'));
    click(byText(container, '位置リセット'));

    expect(onAlarmClear).toHaveBeenCalledWith('AGV-1');
    expect(onPositionReset).toHaveBeenCalledWith('AGV-1');
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

  it('閲覧のみのロールは、緊急停止を押せない', () => {
    const { container } = render(<AgvCard {...props({ permissions: initialRoles.閲覧のみ })} />);

    expect(byText(container, '緊急停止').disabled).toBe(true);
  });
});
