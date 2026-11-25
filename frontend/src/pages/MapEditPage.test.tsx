import { act } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { heading, panelId, type Panel } from '../features/routing/model';
import { change, click, render } from '../testing/render';
import type { MapDraft } from '../features/routing/mapEdit';
import { MapEditPage, type MapEditPageProps } from './MapEditPage';
import { initialRoles } from '../features/auth/permissions';

const panel = (id: string, column: number, row: number, overrides: Partial<Panel> = {}): Panel => ({
  id: panelId(id),
  type: 'Normal',
  hasChargingPin: false,
  column,
  row,
  crossOffsetMm: { x: 0, y: 0 },
  ...overrides,
});

const saved: MapDraft = {
  layout: {
    panelSizeMm: 1000,
    panels: [panel('P1', 0, 0), panel('P2', 1, 0), panel('D1', 1, 1, { type: 'Transfer' })],
    connections: [{ from: panelId('P1'), to: panelId('P2'), distanceMm: 1000, heading: heading(90), oneWay: false }],
  },
  outlines: {},
};

function setup(overrides: Partial<MapEditPageProps> = {}) {
  const handlers = {
    onSaveDraft: vi.fn(() => Promise.resolve<string | null>(null)),
    onReload: vi.fn(),
    onValidate: vi.fn(() => Promise.resolve<readonly string[]>([])),
    onPublish: vi.fn(() => Promise.resolve<string | null>(null)),
    onSaveHeight: vi.fn(),
    onDirtyChange: vi.fn(),
  };
  const view = render(
    <MapEditPage
      saved={saved}
      draftVersion={4}
      savedAt={new Date('2026-11-21T10:12:00')}
      publishedVersion={3}
      equipment={[{ id: 'DEV-1', panel: 'D1' }]}
      heights={{ D1: { heightMm: 320, source: 'measured', at: new Date('2026-11-20T09:00:00') } }}
      running={false}
      travelingAgvs={[]}
      permissions={initialRoles.管理者}
      offline={false}
      {...handlers}
      {...overrides}
    />,
  );
  return { ...view, ...handlers };
}

const byText = (root: Element, text: string) => [...root.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent === text)!;
const tool = (c: HTMLElement, name: string) => c.querySelector<HTMLButtonElement>(`[aria-label="置くもの"] button[data-tool="${name}"]`)!;
const cell = (c: HTMLElement, column: number, row: number) => c.querySelector(`[data-cell="${column},${row}"]`)!;
const target = (c: HTMLElement, id: string) => c.querySelector(`[data-edit-panel="${id}"]`)!;
const side = (c: HTMLElement) => c.querySelector('[aria-labelledby="prop-title"]')!;
const flush = () => act(() => Promise.resolve());

describe('MapEditPage（マップの編集）', () => {
  it('置くものを選んで、空いた格子にパネルを置き、選んだパネルの欄を出す', () => {
    const { container, onDirtyChange } = setup();

    click(tool(container, 'Standby'));
    click(cell(container, 0, 1));

    expect(side(container).textContent).toContain('SB1');
    expect(side(container).querySelector<HTMLSelectElement>('select[aria-label="種類"]')!.value).toBe('Standby');
    expect(onDirtyChange).toHaveBeenLastCalledWith(true);
    expect(byText(container, '下書きを保存する').disabled).toBe(false);
  });

  it('つなぐ：2枚のパネルを順に押す。つなげないときは理由を出す', () => {
    const { container } = setup();

    click(tool(container, 'connect'));
    click(target(container, 'P2'));
    click(target(container, 'D1'));
    click(tool(container, 'select'));
    click(target(container, 'D1'));
    expect(side(container).textContent).toContain('P2 へ');

    click(tool(container, 'connect'));
    click(target(container, 'P1'));
    click(target(container, 'D1'));
    expect(container.textContent).toContain('P1 と D1 は、上下左右に隣り合っていないので、つなげません。');
  });

  it('消す：パネルを押すと、そのパネルとつながりを消す', () => {
    const { container } = setup();

    click(tool(container, 'erase'));
    click(target(container, 'P2'));

    expect(target(container, 'P2')).toBeNull();
    expect(container.querySelectorAll('[data-connection]')).toHaveLength(0);
  });

  it('パネルの欄：受け渡しの相手と、キャリアの高さ（下書きとは別に保存）', () => {
    const { container, onSaveHeight, onDirtyChange } = setup();

    click(target(container, 'D1'));
    expect(side(container).textContent).toContain('DEV-1');
    expect(side(container).textContent).toContain('AGV が測った値');
    change(side(container).querySelector<HTMLInputElement>('input[aria-label="キャリアの高さ"]')!, '350');
    click(byText(side(container), '高さを保存する'));

    expect(onSaveHeight).toHaveBeenCalledWith('D1', 350);
    expect(onDirtyChange).not.toHaveBeenCalledWith(true);
  });

  it('装置やステーションの受け渡しパネルの ID は直せない', () => {
    const { container } = setup();

    click(target(container, 'D1'));
    change(side(container).querySelector<HTMLInputElement>('input[aria-label="パネルの ID"]')!, 'D9');
    click(byText(side(container), 'ID を直す'));

    expect(side(container).textContent).toContain('D1 は装置かステーションの受け渡しパネルなので、ID を直せません。');
  });

  it('下書きを保存する。保存できたら、保存していない変更はなくなる', async () => {
    const { container, onSaveDraft } = setup();

    click(tool(container, 'Normal'));
    click(cell(container, 2, 0));
    click(byText(container, '下書きを保存する'));
    await flush();

    expect(onSaveDraft).toHaveBeenCalledTimes(1);
    expect((onSaveDraft.mock.calls[0] as unknown as [MapDraft])[0].layout.panels.map((p) => p.id)).toContain('P3');
    expect(container.textContent).not.toContain('保存していない変更があります');
  });

  it('下書きの保存を断られたら、理由を出し、変更は残す', async () => {
    const { container } = setup({ onSaveDraft: () => Promise.resolve('ほかの人が下書きを保存しました。') });

    click(tool(container, 'Normal'));
    click(cell(container, 2, 0));
    click(byText(container, '下書きを保存する'));
    await flush();

    expect(container.querySelector('[role="alert"]')?.textContent).toBe('ほかの人が下書きを保存しました。');
    expect(container.textContent).toContain('保存していない変更があります');
  });

  it('読み直す：変更があれば、捨ててよいか確かめてから読み直す', () => {
    const { container, onReload } = setup();

    click(byText(container, 'サーバーから読み直す'));
    expect(onReload).toHaveBeenCalledTimes(1);

    click(tool(container, 'Normal'));
    click(cell(container, 2, 0));
    click(byText(container, 'サーバーから読み直す'));
    expect(onReload).toHaveBeenCalledTimes(1);
    click(byText(container.querySelector('[role="alertdialog"]')!, '変更を捨てて読み直す'));
    expect(onReload).toHaveBeenCalledTimes(2);
  });

  it('サーバーとつながっていなければ、保存も公開もできない', () => {
    const { container } = setup({ offline: true });

    click(tool(container, 'Normal'));
    click(cell(container, 2, 0));

    expect(byText(container, '下書きを保存する').disabled).toBe(true);
    expect(container.textContent).toContain('サーバーとつながっていないので、下書きを保存したり公開したりできません。');
  });

  it('外形は、受け渡しパネルごとに当てる', async () => {
    const { container, onSaveDraft } = setup();

    click(target(container, 'D1'));
    for (const [label, value] of [['外形の左（東へ）', '1000'], ['外形の上（南へ）', '1500'], ['外形の幅', '800'], ['外形の奥行き', '600']] as const) {
      change(side(container).querySelector<HTMLInputElement>(`input[aria-label="${label}"]`)!, value);
    }
    click(byText(side(container), '外形を当てる'));
    click(byText(container, '下書きを保存する'));
    await flush();

    expect((onSaveDraft.mock.calls[0] as unknown as [MapDraft])[0].outlines).toEqual({ D1: { x: 1000, y: 1500, width: 800, height: 600 } });
  });

  it('検証で問題がなければ、確かめる画面を出してから公開する', async () => {
    const { container, onPublish } = setup();
    const publish = () => byText(container, '版4を公開する');

    expect(publish().disabled).toBe(true);
    click(byText(container, '確かめる（検証）'));
    await flush();
    expect(container.textContent).toContain('検証で見つかった問題はありません');

    click(publish());
    click(byText(container.querySelector('[role="alertdialog"]')!, '版4を公開する'));
    expect(onPublish).toHaveBeenCalledTimes(1);
  });

  it('検証で問題があれば、公開できず、問題を出す', async () => {
    const { container } = setup({ onValidate: () => Promise.resolve(['P1 と P2 のつながりに、距離が入っていません。']) });

    click(byText(container, '確かめる（検証）'));
    await flush();

    expect(container.querySelector('[role="alert"]')?.textContent).toContain('距離が入っていません');
    expect(byText(container, '版4を公開する').disabled).toBe(true);
  });

  it('運転中は公開できず、理由を出す', async () => {
    const { container } = setup({ running: true });

    click(byText(container, '確かめる（検証）'));
    await flush();

    expect(byText(container, '版4を公開する').disabled).toBe(true);
    expect(container.textContent).toContain('運転状態を停止にすると、公開できます。');
  });
});
