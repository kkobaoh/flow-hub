import { describe, expect, it, vi } from 'vitest';
import { change, click, render } from '../test/render';
import type { DeviceConfig, ProcessKind, StationConfig, Variety } from './config';
import { ConfigScreen, type ConfigScreenProps } from './ConfigScreen';
import { initialRoles } from './permissions';

const processKinds: ProcessKind[] = [
  { name: '[加工A]', method: 'dedicated', carrier: '[キャリアA]', capacity: 3 },
  { name: '[加工B]', method: 'plate', carrier: '[キャリアB]', capacity: 4 },
];
const varieties: Variety[] = [{ name: '[品種A]', processKind: '[加工A]', ids: ['T-0101', 'T-0102'], stockedIds: ['T-0101'], inUse: true }];
const devices: DeviceConfig[] = [{ id: 'DEV-1', panel: 'D1', layout: { kind: 'slots', slots: [3, 3] }, occupied: [[1], [3]], busy: true }];
const stations: StationConfig[] = [{ id: 'ST-1', panel: 'ST', busy: true }];

function setup(overrides: Partial<ConfigScreenProps> = {}) {
  const handlers = {
    onSaveProcessKind: vi.fn(),
    onDeleteProcessKind: vi.fn(),
    onSaveVariety: vi.fn(),
    onDeleteVariety: vi.fn(),
    onAddId: vi.fn(),
    onRemoveId: vi.fn(),
    onSaveDevice: vi.fn(),
    onDeleteDevice: vi.fn(),
    onSaveStation: vi.fn(),
    onDeleteStation: vi.fn(),
  };
  const view = render(
    <ConfigScreen
      processKinds={processKinds}
      varieties={varieties}
      devices={devices}
      stations={stations}
      carriers={['[キャリアA]', '[キャリアB]']}
      transferPanels={['D1', 'D2', 'ST', 'SP']}
      permissions={initialRoles.管理者}
      {...handlers}
      {...overrides}
    />,
  );
  return { ...view, ...handlers };
}

const byText = (root: Element, text: string) => [...root.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent === text)!;
const tab = (c: HTMLElement, name: string) => [...c.querySelectorAll<HTMLButtonElement>('[role="tab"]')].find((t) => t.textContent === name)!;
const row = (c: HTMLElement, first: string) => [...c.querySelectorAll('tbody tr')].find((r) => r.querySelector('td')?.textContent === first)!;
const dialog = (c: HTMLElement) => c.querySelector('[role="dialog"]')!;
const input = (root: Element, label: string) => root.querySelector<HTMLInputElement>(`input[aria-label="${label}"]`)!;
const select = (root: Element, label: string) => root.querySelector<HTMLSelectElement>(`select[aria-label="${label}"]`)!;

describe('ConfigScreen（消耗品の構成）', () => {
  it('加工の種類を加える', () => {
    const { container, onSaveProcessKind } = setup();

    click(byText(container, '加工の種類を加える'));
    change(input(dialog(container), '加工の種類の名前'), '[加工C]');
    change(select(dialog(container), '運び方'), 'dedicated');
    change(select(dialog(container), 'キャリアの種類'), '[キャリアA]');
    change(input(dialog(container), 'キャリアに入る個数'), '2');
    click(byText(dialog(container), '加工の種類を保存する'));

    expect(onSaveProcessKind).toHaveBeenCalledWith(null, { name: '[加工C]', method: 'dedicated', carrier: '[キャリアA]', capacity: 2 });
  });

  it('品種がある加工の種類は消せず、ないものは確かめてから消す', () => {
    const { container, onDeleteProcessKind } = setup();

    expect(byText(row(container, '[加工A]'), '消す').disabled).toBe(true);
    expect(row(container, '[加工A]').textContent).toContain('[品種A] の加工の種類なので、消せません。');

    click(byText(row(container, '[加工B]'), '消す'));
    click(byText(container.querySelector('[role="alertdialog"]')!, '加工の種類を消す'));
    expect(onDeleteProcessKind).toHaveBeenCalledWith('[加工B]');
  });

  it('品種の ID の対応：加えるのと外す。在庫にある ID は外せない', () => {
    const { container, onAddId, onRemoveId } = setup();

    click(tab(container, '品種'));
    expect(row(container, '[品種A]').textContent).toContain('2');
    click(byText(row(container, '[品種A]'), 'ID の対応'));
    const d = dialog(container);
    const idRow = (id: string) => [...d.querySelectorAll('li')].find((li) => li.textContent?.startsWith(id))!;
    expect(byText(idRow('T-0101'), '外す').disabled).toBe(true);
    click(byText(idRow('T-0102'), '外す'));
    expect(onRemoveId).toHaveBeenCalledWith('[品種A]', 'T-0102');

    change(input(d, '加える ID'), 'T-0101');
    expect(byText(d, '加える').disabled).toBe(true);
    change(input(d, '加える ID'), 'T-0103');
    click(byText(d, '加える'));
    expect(onAddId).toHaveBeenCalledWith('[品種A]', 'T-0103');
  });

  it('装置を加える：ストッカーごとにスロットの数を入れ、ストッカーを加えたり外したりできる', () => {
    const { container, onSaveDevice } = setup();

    click(tab(container, '装置'));
    click(byText(container, '装置を加える'));
    const d = dialog(container);
    change(input(d, '装置の ID'), 'DEV-2');
    expect([...select(d, '受け渡しパネル').options].map((o) => o.value)).toEqual(['', 'D2', 'SP']);
    change(select(d, '受け渡しパネル'), 'D2');
    change(input(d, 'ストッカー1 のスロットの数'), '3');
    click(byText(d, 'ストッカーを加える'));
    change(input(d, 'ストッカー2 のスロットの数'), '4');
    click(byText(d, 'ストッカーを加える'));
    click(d.querySelector('button[aria-label="ストッカー3 を外す"]')!);
    click(byText(d, '装置を保存する'));

    expect(onSaveDevice).toHaveBeenCalledWith(null, { id: 'DEV-2', panel: 'D2', layout: { kind: 'slots', slots: [3, 4] } });
  });

  it('プレートに載せたまま置く装置は、ストッカーの数だけ入れる', () => {
    const { container, onSaveDevice } = setup();

    click(tab(container, '装置'));
    click(byText(container, '装置を加える'));
    const d = dialog(container);
    change(input(d, '装置の ID'), 'DEV-3');
    change(select(d, '受け渡しパネル'), 'SP');
    click(d.querySelector('input[type="checkbox"]')!);
    expect(input(d, 'ストッカー1 のスロットの数')).toBeNull();
    change(input(d, 'ストッカーの数'), '2');
    click(byText(d, '装置を保存する'));

    expect(onSaveDevice).toHaveBeenCalledWith(null, { id: 'DEV-3', panel: 'SP', layout: { kind: 'plate', stockers: 2 } });
  });

  it('装置を変える：ID は変えられず、消耗品があるスロットは減らせない', () => {
    const { container } = setup();

    click(tab(container, '装置'));
    click(byText(row(container, 'DEV-1'), '変える'));
    const d = dialog(container);
    expect(input(d, '装置の ID')).toBeNull();
    change(input(d, 'ストッカー2 のスロットの数'), '2');

    expect(byText(d, '装置を保存する').disabled).toBe(true);
    expect(d.textContent).toContain('ストッカー2 のスロット3 に消耗品があるので、減らせません。');
  });

  it('ステーションを加える', () => {
    const { container, onSaveStation } = setup();

    click(tab(container, 'ステーション'));
    click(byText(container, 'ステーションを加える'));
    change(input(dialog(container), 'ステーションの ID'), 'ST-2');
    change(select(dialog(container), '受け渡しパネル'), 'D2');
    click(byText(dialog(container), 'ステーションを保存する'));

    expect(onSaveStation).toHaveBeenCalledWith(null, { id: 'ST-2', panel: 'D2' });
  });

  it('管理者でなければ、どの操作も使えず、理由を出す', () => {
    const { container } = setup({ permissions: initialRoles.保守 });

    expect(byText(container, '加工の種類を加える').disabled).toBe(true);
    expect(byText(row(container, '[加工B]'), '消す').disabled).toBe(true);
    expect(container.textContent).toContain('消耗品の構成は、管理者のロールで使えます。');
  });
});
