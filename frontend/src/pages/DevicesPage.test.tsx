import { act } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { equipmentId } from '../features/tool/equipment';
import { panelId } from '../features/routing/model';
import { change, click, render } from '../testing/render';
import type { DeviceInside } from '../features/tool/devices';
import { DevicesPage, type DevicesPageProps } from './DevicesPage';
import { initialRoles } from '../features/auth/permissions';

const dev1: DeviceInside = {
  id: equipmentId('DEV-1'),
  panel: panelId('D1'),
  processing: [{ id: 'T-0100', variety: '[品種A]', note: '中古品・取り込み中', tone: 'normal' }],
  stockers: [
    {
      name: 'ストッカー1',
      layout: 'slots',
      slots: [
        { number: 1, content: { kind: 'held', consumable: { id: 'T-0101', variety: '[品種A]', note: '新品', tone: 'normal' } } },
        { number: 2, content: { kind: 'empty' } },
        { number: 3, content: { kind: 'held', consumable: { id: null, variety: '不明な消耗品', note: 'ID が食い違った', tone: 'blocked' } } },
      ],
    },
  ],
  handover: [],
  setting: [
    { variety: '[品種A]', target: 2, have: 1, status: 'short' },
    { variety: '[品種D]', target: 0, have: 1, status: 'notInSetting' },
  ],
};

const dev2: DeviceInside = {
  id: equipmentId('DEV-2'),
  panel: panelId('D2'),
  processing: [],
  stockers: [{ name: 'ストッカー1', layout: 'plate', items: [{ id: 'T-0300', variety: '[品種C]', note: '新品', tone: 'normal' }] }],
  handover: [],
  setting: [],
};

function setup(overrides: Partial<DevicesPageProps> = {}) {
  const onSelect = vi.fn();
  const onSaveSetting = vi.fn<DevicesPageProps['onSaveSetting']>().mockResolvedValue(null);
  const view = render(
    <DevicesPage
      devices={[dev2, dev1]}
      selected={null}
      varieties={['[品種A]', '[品種B]', '[品種C]', '[品種D]']}
      permissions={initialRoles.運用}
      offline={false}
      onSelect={onSelect}
      onSaveSetting={onSaveSetting}
      {...overrides}
    />,
  );
  return { ...view, onSelect, onSaveSetting };
}

const byText = (c: HTMLElement, text: string) => [...c.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent === text)!;
const flush = () =>
  act(async () => {
    await Promise.resolve();
  });
const deviceSelect = (c: HTMLElement) => c.querySelector<HTMLSelectElement>('select[aria-label="装置"]')!;

describe('DevicesPage（装置とストッカー）', () => {
  it('装置を番号の順に選べ、選んだ装置を知らせる', () => {
    const { container, onSelect } = setup();

    expect([...deviceSelect(container).options].map((o) => o.text)).toEqual(['DEV-1（受け渡しパネル D1）', 'DEV-2（受け渡しパネル D2）']);
    expect(deviceSelect(container).value).toBe('DEV-1');

    change(deviceSelect(container), 'DEV-2');
    expect(onSelect).toHaveBeenCalledWith('DEV-2');
  });

  it('加工部、スロット、受け渡し部の中身を出す', () => {
    const { container } = setup();
    const inside = container.querySelector('[aria-labelledby="inside-title"]')!;

    expect(inside.textContent).toContain('T-0100');
    expect(inside.textContent).toContain('スロット2');
    expect(inside.textContent).toContain('不明な消耗品');
    expect(inside.textContent).toContain('受け渡し部空き');
  });

  it('プレートに載せたまま置くストッカーは、置いてある消耗品を並べる', () => {
    const { container } = setup({ selected: 'DEV-2' });

    expect(container.textContent).toContain('T-0300');
    expect(container.textContent).not.toContain('スロット1');
  });

  it('設定の様子を、品種ごとに出す', () => {
    const { container } = setup();
    const rows = [...container.querySelectorAll('[aria-labelledby="setting-title"] tbody tr')].map((r) => r.textContent);

    expect(rows).toEqual(['[品種A]21足りない（届ける）', '[品種D]－1設定にない（回収する）']);
  });

  it('設定を変えて保存する。0個の品種は外し、外すボタンでも外せる', async () => {
    const { container, onSaveSetting } = setup();

    click(byText(container, '設定を変える（段取り替え）'));
    change(container.querySelector<HTMLInputElement>('input[aria-label="[品種A] の設定の個数"]')!, '0');
    change(container.querySelector<HTMLSelectElement>('select[aria-label="加える品種"]')!, '[品種B]');
    click(byText(container, '加える'));
    change(container.querySelector<HTMLInputElement>('input[aria-label="[品種B] の設定の個数"]')!, '2');
    change(container.querySelector<HTMLSelectElement>('select[aria-label="加える品種"]')!, '[品種C]');
    click(byText(container, '加える'));
    click(container.querySelector('button[aria-label="[品種C] を設定から外す"]')!);
    click(byText(container, '設定を保存する'));
    await flush();

    expect(onSaveSetting).toHaveBeenCalledWith('DEV-1', [{ variety: '[品種B]', target: 2 }]);
    expect(container.querySelector('input[type="number"]')).toBeNull();
  });

  it('サーバーが断ったら、変えた内容のまま理由を出す', async () => {
    const onSaveSetting = vi.fn<DevicesPageProps['onSaveSetting']>().mockResolvedValue('個数の合計がスロットの数を超えているので、保存できませんでした。');
    const { container } = setup({ onSaveSetting });

    click(byText(container, '設定を変える（段取り替え）'));
    click(byText(container, '設定を保存する'));
    await flush();

    expect(container.querySelector('[role="alert"]')?.textContent).toBe('個数の合計がスロットの数を超えているので、保存できませんでした。');
    expect(container.querySelector<HTMLInputElement>('input[aria-label="[品種A] の設定の個数"]')?.value).toBe('2');
  });

  it('送って結果が返るまでは、もう一度保存できない', async () => {
    let answer: (notice: string | null) => void = () => {};
    const onSaveSetting = vi.fn<DevicesPageProps['onSaveSetting']>(() => new Promise((resolve) => (answer = resolve)));
    const { container } = setup({ onSaveSetting });

    click(byText(container, '設定を変える（段取り替え）'));
    click(byText(container, '設定を保存する'));
    expect(byText(container, '保存しています').disabled).toBe(true);

    await act(async () => answer(null));
    expect(container.querySelector('input[type="number"]')).toBeNull();
  });

  it('サーバーとつながっていないときは、設定を変えられず、理由を出す', () => {
    const { container } = setup({ offline: true });

    expect(byText(container, '設定を変える（段取り替え）').disabled).toBe(true);
    expect(container.textContent).toContain('サーバーとつながっていないので、設定を変えられません。');
  });

  it('スロットごとに、番号と中身（ある消耗品か空き）を出す', () => {
    const { container } = setup();
    const slots = [...container.querySelectorAll('[aria-labelledby="inside-title"] [data-slot]')].map((s) => s.textContent);

    expect(slots).toEqual(['スロット1[品種A]T-0101新品', 'スロット2空き', 'スロット3不明な消耗品ID が食い違った']);
  });

  it('合計がスロットの数を超えたら、保存できず理由を出す', () => {
    const { container } = setup();

    click(byText(container, '設定を変える（段取り替え）'));
    change(container.querySelector<HTMLInputElement>('input[aria-label="[品種A] の設定の個数"]')!, '4');

    expect(byText(container, '設定を保存する').disabled).toBe(true);
    expect(container.textContent).toContain('合計 4個が、スロットの数（3）を超えています。');
  });

  it('やめると、変えた内容を捨てる', () => {
    const { container, onSaveSetting } = setup();

    click(byText(container, '設定を変える（段取り替え）'));
    change(container.querySelector<HTMLInputElement>('input[aria-label="[品種A] の設定の個数"]')!, '1');
    click(byText(container, 'やめる'));

    expect(onSaveSetting).not.toHaveBeenCalled();
    click(byText(container, '設定を変える（段取り替え）'));
    expect(container.querySelector<HTMLInputElement>('input[aria-label="[品種A] の設定の個数"]')!.value).toBe('2');
  });

  it('パーミッションがなければ、設定を変えられず、理由を出す', () => {
    const { container } = setup({ permissions: initialRoles.閲覧のみ });

    expect(byText(container, '設定を変える（段取り替え）').disabled).toBe(true);
    expect(container.textContent).toContain('運用以上のロールで使えます');
  });
});
