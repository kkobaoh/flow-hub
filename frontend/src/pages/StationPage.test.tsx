import { describe, expect, it, vi } from 'vitest';
import { equipmentId } from '../features/tool/equipment';
import { panelId } from '../features/routing/model';
import { change, click, render } from '../testing/render';
import { initialRoles } from '../features/auth/permissions';
import type { StationInventory } from '../features/tool/station';
import { StationPage, type StationPageProps } from './StationPage';

const at = new Date('2026-11-20T14:02:00');

const st1: StationInventory = {
  id: equipmentId('ST-1'),
  panel: panelId('ST'),
  summary: [{ variety: '[品種A]', available: 1, reserved: 1, replenishWaiting: 2 }],
  items: [
    { id: 'T-0111', variety: '[品種A]', condition: '新品', receivedAt: at, state: { kind: 'reserved', transport: 14, assigned: true } },
    { id: 'T-0110', variety: '[品種A]', condition: '新品', receivedAt: at, state: { kind: 'available' } },
    { id: 'X-9001', variety: null, condition: null, receivedAt: at, state: { kind: 'needsCheck', reason: '登録されていない ID として入ってきました。' } },
  ],
};

function setup(overrides: Partial<StationPageProps> = {}) {
  const onShip = vi.fn();
  const onFix = vi.fn();
  const view = render(
    <StationPage stations={[st1]} selected={null} varieties={['[品種A]', '[品種B]']} permissions={initialRoles.運用} onSelect={() => {}} onShip={onShip} onFix={onFix} {...overrides} />,
  );
  return { ...view, onShip, onFix };
}

const byText = (root: Element, text: string) => [...root.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent === text)!;
const ids = (c: HTMLElement) => [...c.querySelectorAll('[aria-labelledby="items-title"] tbody tr')].map((r) => r.querySelector('td')?.textContent);
const rowOf = (c: HTMLElement, id: string) => [...c.querySelectorAll('[aria-labelledby="items-title"] tbody tr')].find((r) => r.querySelector('td')?.textContent === id)!;

describe('StationPage（ステーションの在庫）', () => {
  it('品種ごとの数を出す', () => {
    const { container } = setup();

    expect(container.querySelector('[aria-labelledby="summary-title"] tbody tr')?.textContent).toBe('[品種A]112');
  });

  it('消耗品を、要確認を上にして ID の順に並べ、要確認だけにも絞れる', () => {
    const { container } = setup();

    expect(ids(container)).toEqual(['X-9001', 'T-0110', 'T-0111']);

    click(byText(container, '要確認だけ（1）'));
    expect(ids(container)).toEqual(['X-9001']);
  });

  it('出庫は、確かめる画面で選んだときだけ頼む', () => {
    const { container, onShip } = setup();

    click(byText(rowOf(container, 'T-0110'), '出庫する'));
    const dialog = container.querySelector('[role="alertdialog"]')!;
    expect(dialog.textContent).toContain('T-0110');
    click(byText(dialog, '出庫する'));

    expect(onShip).toHaveBeenCalledWith('ST-1', 'T-0110');
  });

  it('届ける搬送に AGV を割り当て済みのものは、出庫できず理由を出す', () => {
    const { container } = setup();

    expect(byText(rowOf(container, 'T-0111'), '出庫する').disabled).toBe(true);
    expect(rowOf(container, 'T-0111').textContent).toContain('AGV を割り当て済みなので、出庫できません。');
  });

  it('要確認の消耗品を直す。品種と新品・中古品を選ぶまで保存できない', () => {
    const { container, onFix } = setup();

    click(byText(rowOf(container, 'X-9001'), '確かめて直す'));
    const dialog = container.querySelector('[role="dialog"]')!;
    expect(dialog.textContent).toContain('登録されていない ID として入ってきました。');
    const save = byText(dialog, '直して、要確認を外す');
    expect(save.disabled).toBe(true);

    change(dialog.querySelector<HTMLSelectElement>('select')!, '[品種B]');
    expect(save.disabled).toBe(true);
    click(dialog.querySelector<HTMLInputElement>('input[value="中古品"]')!);
    click(save);

    expect(onFix).toHaveBeenCalledWith('ST-1', 'X-9001', { variety: '[品種B]', condition: '中古品' });
    expect(container.querySelector('[role="dialog"]')).toBeNull();
  });

  it('パーミッションがなければ、出庫も直すのもできない', () => {
    const { container } = setup({ permissions: initialRoles.閲覧のみ });

    expect(byText(rowOf(container, 'T-0110'), '出庫する').disabled).toBe(true);
    expect(byText(rowOf(container, 'X-9001'), '確かめて直す').disabled).toBe(true);
    expect(container.textContent).toContain('運用以上のロールで使えます');
  });
});
