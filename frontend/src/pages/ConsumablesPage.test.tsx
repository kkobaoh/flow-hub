import { act } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { change, click, render } from '../testing/render';
import type { ConsumableRecord } from '../features/tool/consumables';
import { ConsumablesPage, type ConsumablesPageProps } from './ConsumablesPage';
import { demoDevices } from '../app/demo/demo';
import { initialRoles } from '../features/auth/permissions';

const base: ConsumableRecord = {
  id: 'T-0101',
  variety: '[品種A]',
  processKind: '[加工の種類A]',
  condition: '新品',
  location: { kind: 'slot', device: 'DEV-1', stocker: 1, slot: 1 },
  needsCheck: false,
  reservation: null,
  nomination: null,
  shipping: false,
};

const records: ConsumableRecord[] = [
  base,
  { ...base, id: 'T-0110', location: { kind: 'station', station: 'ST-1' } },
  { ...base, id: 'T-0111', location: { kind: 'station', station: 'ST-1' }, nomination: { device: 'DEV-2', assigned: false } },
];

function setup(overrides: Partial<ConsumablesPageProps> = {}) {
  const handlers = {
    onOpen: vi.fn(),
    onNominate: vi.fn<ConsumablesPageProps['onNominate']>().mockResolvedValue(null),
    onCancelNomination: vi.fn<ConsumablesPageProps['onCancelNomination']>().mockResolvedValue(null),
    onRegister: vi.fn<ConsumablesPageProps['onRegister']>().mockResolvedValue(null),
    onEdit: vi.fn<ConsumablesPageProps['onEdit']>().mockResolvedValue(null),
    onDelete: vi.fn<ConsumablesPageProps['onDelete']>().mockResolvedValue(null),
  };
  const view = render(
    <ConsumablesPage
      records={records}
      selected={null}
      stations={['ST-1']}
      devices={demoDevices}
      varieties={['[品種A]', '[品種B]']}
      permissions={initialRoles.保守}
      offline={false}
      {...handlers}
      {...overrides}
    />,
  );
  return { ...view, ...handlers };
}

const byText = (root: Element, text: string) => [...root.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent === text)!;
const search = (c: HTMLElement, q: string) => {
  change(c.querySelector<HTMLInputElement>('input[type="search"]')!, q);
  click(byText(c, '探す'));
};

describe('ConsumablesPage（消耗品の検索と手入力）', () => {
  it('ID がぴったり合うものがあれば、すぐに開く', () => {
    const { container, onOpen } = setup();

    search(container, 't-0110');

    expect(onOpen).toHaveBeenCalledWith('T-0110');
  });

  it('一部だけ合うときは、候補を ID の順に並べ、選んで開く', () => {
    const { container, onOpen } = setup();

    search(container, '011');
    const candidates = [...container.querySelectorAll('[aria-label="見つかった消耗品"] button')].map((b) => b.textContent);
    expect(candidates.map((t) => t?.slice(0, 6))).toEqual(['T-0110', 'T-0111']);

    click(container.querySelector('[aria-label="見つかった消耗品"] button')!);
    expect(onOpen).toHaveBeenCalledWith('T-0110');
  });

  it('合うものがなければ、そのことを出す', () => {
    const { container } = setup();

    search(container, 'Z');

    expect(container.textContent).toContain('「Z」に合う消耗品はありません。');
  });

  it('開いた消耗品の所在、予約、指名を出す。これまでの動きは、まだ記録していないと出す', () => {
    const { container } = setup({ selected: 'T-0101' });
    const item = container.querySelector('[aria-labelledby="item-title"]')!;

    expect(item.textContent).toContain('DEV-1 のストッカー1・スロット1');
    expect(item.textContent).toContain('[加工の種類A]');
    expect(container.querySelector('[aria-labelledby="history-title"]')?.textContent).toContain('これまでの動きは、まだ記録していません');
  });

  it('予約は、入っている搬送の番号と目的を出す。番号がわからなければ目的だけ', () => {
    const reserved = (transport: number | null): ConsumableRecord => ({ ...base, reservation: { transport, assigned: false, purpose: 'DEV-1 へ届ける' } });

    expect(setup({ records: [reserved(3)], selected: 'T-0101' }).container.textContent).toContain('搬送 #3（DEV-1 へ届ける）');
    expect(setup({ records: [reserved(null)], selected: 'T-0101' }).container.textContent).toContain('予約DEV-1 へ届ける');
  });

  it('ステーションにある新品は、装置を選んで指名できる', async () => {
    const { container, onNominate } = setup({ selected: 'T-0110' });
    const nominate = byText(container, '指名する');

    expect(nominate.disabled).toBe(true);
    change(container.querySelector<HTMLSelectElement>('select[aria-label="届ける装置"]')!, 'DEV-2');
    await act(async () => {
      click(byText(container, '指名する'));
      await Promise.resolve();
    });

    expect(onNominate).toHaveBeenCalledWith('T-0110', 'DEV-2');
  });

  it('指名をサーバーが断ったら、理由を出す', async () => {
    const onNominate = vi.fn<ConsumablesPageProps['onNominate']>().mockResolvedValue('指名できませんでした。');
    const { container } = setup({ selected: 'T-0110', onNominate });

    change(container.querySelector<HTMLSelectElement>('select[aria-label="届ける装置"]')!, 'DEV-2');
    await act(async () => {
      click(byText(container, '指名する'));
      await Promise.resolve();
    });

    expect(container.querySelector('[aria-labelledby="item-title"] [role="alert"]')?.textContent).toBe('指名できませんでした。');
  });

  it('サーバーとつながっていないときは、指名も取り消しもできず、理由を出す', () => {
    const { container } = setup({ selected: 'T-0110', offline: true });

    expect(container.querySelector<HTMLSelectElement>('select[aria-label="届ける装置"]')!.disabled).toBe(true);
    expect(container.textContent).toContain('サーバーとつながっていないので、指名も取り消しもできません。');
  });

  it('サーバーとつながっていないときは、登録も直すも消すも押せず、理由を出す', () => {
    const { container } = setup({ selected: 'T-0110', offline: true });

    expect(byText(container, '手入力で登録する').disabled).toBe(true);
    expect(byText(container, '記録を直す').disabled).toBe(true);
    expect(byText(container, '記録を消す').disabled).toBe(true);
    expect(container.textContent).toContain('サーバーとつながっていないので、在庫の手入力はできません。');
  });

  it('在庫の手入力をサーバーが断ったら、理由を出す', async () => {
    const onDelete = vi.fn<ConsumablesPageProps['onDelete']>().mockResolvedValue('記録にないので、消せませんでした。');
    const { container } = setup({ selected: 'T-0110', onDelete });

    click(byText(container, '記録を消す'));
    await act(async () => {
      click(byText(container.querySelector('[role="alertdialog"]')!, '記録を消す'));
      await Promise.resolve();
    });

    expect(container.querySelector('[data-manual-notice]')?.textContent).toBe('記録にないので、消せませんでした。');
  });

  it('装置にあるものは指名できず、理由を出す', () => {
    const { container } = setup({ selected: 'T-0101' });

    expect(byText(container, '指名する').disabled).toBe(true);
    expect(container.textContent).toContain('指名できるのは、ステーションにある');
  });

  it('指名してあれば、取り消せる', async () => {
    const { container, onCancelNomination } = setup({ selected: 'T-0111' });

    expect(container.textContent).toContain('DEV-2 へ届けるものに指名');
    await act(async () => {
      click(byText(container, '指名を取り消す'));
      await Promise.resolve();
    });

    expect(onCancelNomination).toHaveBeenCalledWith('T-0111');
  });

  it('記録を直す：いまの記録を入れておき、変えたものを保存する', () => {
    const { container, onEdit } = setup({ selected: 'T-0110' });

    click(byText(container, '記録を直す'));
    const dialog = container.querySelector('[role="dialog"]')!;
    const save = byText(dialog, '記録を保存する');
    expect(dialog.querySelector<HTMLSelectElement>('select[aria-label="品種"]')!.value).toBe('[品種A]');
    expect(save.disabled).toBe(true);

    click(dialog.querySelector<HTMLInputElement>('input[value="中古品"]')!);
    click(save);

    expect(onEdit).toHaveBeenCalledWith({ id: 'T-0110', variety: '[品種A]', condition: '中古品', location: { kind: 'station', station: 'ST-1' } });
  });

  it('記録を消すのは、確かめる画面で選んだときだけ', () => {
    const { container, onDelete } = setup({ selected: 'T-0110' });

    click(byText(container, '記録を消す'));
    click(byText(container.querySelector('[role="alertdialog"]')!, '記録を消す'));

    expect(onDelete).toHaveBeenCalledWith('T-0110');
  });

  it('手入力で登録する：すべて入れるまで保存できない', () => {
    const { container, onRegister } = setup();

    click(byText(container, '手入力で登録する'));
    const dialog = container.querySelector('[role="dialog"]')!;
    const save = byText(dialog, '記録を保存する');
    expect(save.disabled).toBe(true);

    change(dialog.querySelector<HTMLInputElement>('input[aria-label="ID"]')!, 'T-0500');
    change(dialog.querySelector<HTMLSelectElement>('select[aria-label="品種"]')!, '[品種B]');
    click(dialog.querySelector<HTMLInputElement>('input[value="新品"]')!);
    const where = dialog.querySelector<HTMLSelectElement>('select[aria-label="所在"]')!;
    change(where, [...where.options].find((o) => o.text === 'DEV-2 のストッカー1・スロット4')?.value ?? '');
    expect(save.disabled).toBe(true);
    change(where, [...where.options].find((o) => o.text === 'ST-1')!.value);
    click(save);

    expect(onRegister).toHaveBeenCalledWith({ id: 'T-0500', variety: '[品種B]', condition: '新品', location: { kind: 'station', station: 'ST-1' } });
  });

  it('保守より下のロールでは、手入力を使えず、理由を出す', () => {
    const { container } = setup({ selected: 'T-0110', permissions: initialRoles.運用 });

    expect(byText(container, '手入力で登録する').disabled).toBe(true);
    expect(byText(container, '記録を直す').disabled).toBe(true);
    expect(container.textContent).toContain('保守以上のロールで使えます');
  });
});
