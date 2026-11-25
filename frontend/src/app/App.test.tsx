import { act } from 'react';
import { describe, expect, it } from 'vitest';
import { click, render } from '../testing/render';
import { App } from './App';
import { createFakeLink, type FakeLink } from '../testing/fakeLink';
import type { MapSnapshot } from '../features/routing/api/mapSnapshot';
import type { DeviceStatus, EquipmentStatusSnapshot } from '../features/tool/api/equipmentStatus';
import { sampleEquipment, sampleLayout } from './demo/sampleLayout';

/** サーバーから届いたことにするマップ（見本と同じ並び。気をつけることは、まだ届かないので空）。 */
const serverMap: MapSnapshot = { version: 1, mapVersion: 3, layout: sampleLayout, equipment: sampleEquipment.map((e) => ({ ...e, attentions: [] })) };

/** サーバーから届いたことにする装置の様子。 */
const deviceStatus = (id: string, more: Partial<DeviceStatus> = {}): DeviceStatus => ({
  id,
  panel: 'D1',
  shortage: 0,
  collectWaiting: 0,
  mismatchedSlots: [],
  setupErrors: [],
  stocker: [{ variety: 'DR-5', target: 1, have: 1 }],
  slots: { total: 2, free: 1, delivering: 0 },
  processing: [],
  stockerSlots: [
    { stocker: 1, slot: 1, consumable: { id: 'T-1', variety: 'DR-5', condition: 'new', mismatched: false, discardRequested: false, recovery: { state: 'none' } } },
    { stocker: 1, slot: 2, consumable: null },
  ],
  handover: [],
  ...more,
});

/** サーバーから届いたことにする装置とステーションの様子。 */
const equipmentStatus: EquipmentStatusSnapshot = {
  version: 1,
  devices: [deviceStatus('DEV-1'), deviceStatus('DEV-2', { panel: 'D2' })],
  stations: [
    {
      id: 'ST-1',
      panel: 'ST',
      needsCheck: 1,
      replenishWaiting: 0,
      stock: [{ variety: 'DR-5', available: 0, reserved: 1, replenishWaiting: 0 }],
      items: [
        { id: 'T-5', variety: 'DR-5', condition: 'new', arrivedAt: new Date('2026-11-25T09:30:00Z'), state: { kind: 'reserved', device: 'DEV-1' } },
        { id: 'X-1', variety: null, condition: null, arrivedAt: new Date('2026-11-25T08:12:00Z'), state: { kind: 'needsCheck', reason: 'idMismatch' } },
      ],
    },
  ],
  varieties: ['DR-5', 'DR-6'],
};

/** つながって、運転中の様子が届いた偽物のつながり。 */
const connected = (): FakeLink =>
  createFakeLink({ connection: 'connected', status: { version: 1, state: 'started' }, map: serverMap, transports: { version: 1, transports: [] } });

function buttonByText(root: ParentNode, text: string): HTMLButtonElement {
  const found = [...root.querySelectorAll('button')].find((b) => b.textContent === text);
  if (!found) {
    throw new Error(`「${text}」のボタンがありません`);
  }
  return found;
}

const runSwitch = (c: HTMLElement) => c.querySelector<HTMLElement>('[role="group"][aria-label="運転状態"]')!;
const headerStatus = (c: HTMLElement) => c.querySelector('header [role="status"]')?.textContent;

/** 運転の切り替えで「停止」を押し、確かめる画面で「停止する」を押す。 */
async function stopFromSwitch(c: HTMLElement) {
  click(buttonByText(runSwitch(c), '停止'));
  await act(async () => {
    click(buttonByText(c.querySelector('[role="alertdialog"]')!, '停止する'));
    await Promise.resolve();
  });
}

describe('App', () => {
  it('入り口は、全体の様子（マップ）の画面にする', () => {
    const { container } = render(<App link={connected()} />);

    expect(container.querySelector('header')?.textContent).toContain('FlowHub');
    expect(container.querySelector('svg[aria-label="マップ"]')).not.toBeNull();
  });

  it('メニューで選んだ画面に切り替え、URL の # のあとに覚える', () => {
    window.location.hash = '';
    const { container } = render(<App link={connected()} />);

    click(container.querySelector('button[aria-controls="main-menu"]')!);
    click(container.querySelector('[data-screen="transports"]')!);

    expect(container.querySelector('h1')?.textContent).toBe('搬送の一覧');
    expect(container.querySelector('[role="tablist"][aria-label="搬送の段階"]')).not.toBeNull();
    expect(window.location.hash).toBe('#/transports');

    click(container.querySelector('button[aria-controls="main-menu"]')!);
    click(container.querySelector('[data-screen="map"]')!);
    expect(container.querySelector('svg[aria-label="マップ"]')).not.toBeNull();
  });

  it('搬送の一覧は、届いた搬送で出し、取りやめをサーバーへ送って、結果の知らせを出す', async () => {
    window.location.hash = '#/transports';
    const link = createFakeLink({ connection: 'connected', status: { version: 1, state: 'started' }, map: serverMap });
    const { container } = render(<App link={link} />);
    expect(container.textContent).toContain('搬送の一覧を読み込んでいます。');

    act(() =>
      link.emitTransports({
        version: 1,
        transports: [
          {
            id: 12,
            source: 'ST-1',
            destination: 'DEV-1',
            neededBy: new Date('2026-11-25T10:00:00Z'),
            purpose: 'delivery',
            cargo: [{ variety: 'DR-5', count: 2 }],
            stage: 'movingToSource',
            agv: 'AGV-1',
            paused: false,
          },
        ],
      }),
    );
    const row = container.querySelector('tbody tr')!;
    expect(row.textContent).toContain('ST-1 から DEV-1');
    expect(row.textContent).toContain('DR-5 2個');
    expect(row.textContent).toContain('搬送元へ移動');

    link.respondToCancel('canceling');
    click(buttonByText(row, '取りやめる'));
    await act(async () => {
      click(buttonByText(container.querySelector('[role="alertdialog"]')!, '取りやめる'));
      await Promise.resolve();
    });

    expect(link.canceledTransports).toEqual([12]);
    expect(container.querySelector('main [role="status"]')?.textContent).toContain('キャリアを上げたところで止まります');
    window.location.hash = '';
  });

  it('消耗品の検索は、届いた消耗品の記録で開き、指名をサーバーへ送る', async () => {
    window.location.hash = '#/consumables/T-5';
    const link = connected();
    const { container } = render(<App link={link} />);
    expect(container.textContent).toContain('消耗品の記録を読み込んでいます。');

    act(() => {
      link.emitEquipment(equipmentStatus);
      link.emitConsumables({
        version: 1,
        consumables: [
          {
            id: 'T-5',
            variety: 'DR-5',
            processType: 'drill',
            condition: 'new',
            location: { kind: 'station', station: 'ST-1' },
            checkReason: null,
            reservation: null,
            designation: null,
            shipping: false,
          },
        ],
      });
    });
    const item = container.querySelector('[aria-labelledby="item-title"]')!;
    expect(item.textContent).toContain('ST-1');
    expect(buttonByText(container, '手入力で登録する').disabled).toBe(false);

    const select = item.querySelector<HTMLSelectElement>('select[aria-label="届ける装置"]')!;
    act(() => {
      select.value = 'DEV-2';
      select.dispatchEvent(new Event('change', { bubbles: true }));
    });
    await act(async () => {
      click(buttonByText(item, '指名する'));
      await Promise.resolve();
    });

    expect(link.consumableCommands).toEqual([{ kind: 'designate', consumable: 'T-5', device: 'DEV-2' }]);
    window.location.hash = '';
  });

  it('在庫の手入力で消したら、サーバーへ送り、開いていた消耗品を閉じる', async () => {
    window.location.hash = '#/consumables/T-5';
    const link = connected();
    const { container } = render(<App link={link} />);
    act(() => {
      link.emitEquipment(equipmentStatus);
      link.emitConsumables({
        version: 1,
        consumables: [
          { id: 'T-5', variety: 'DR-5', processType: 'drill', condition: 'new', location: { kind: 'station', station: 'ST-1' }, checkReason: null, reservation: null, designation: null, shipping: false },
        ],
      });
    });

    click(buttonByText(container, '記録を消す'));
    await act(async () => {
      click(buttonByText(container.querySelector('[role="alertdialog"]')!, '記録を消す'));
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(link.consumableCommands).toEqual([{ kind: 'delete', consumable: 'T-5' }]);
    expect(window.location.hash).toBe('#/consumables');
    window.location.hash = '';
  });

  it('AGV と AGV の種類の登録は、届いた登録で出し、種類を消すのをサーバーへ送って、断られたら理由を出す', async () => {
    window.location.hash = '#/agvs';
    const link = connected();
    const { container } = render(<App link={link} />);
    expect(container.textContent).toContain('AGV と AGV の種類の登録を読み込んでいます。');

    act(() =>
      link.emitFleet({
        version: 1,
        types: [{ id: 'small', name: '小さい AGV', forward: 500, backward: 250, rotation: 90, chargeAt: 20, resumeAt: 80, special: false }],
        entries: [],
        defaults: { chargeAt: 20, resumeAt: 80 },
        carriers: ['X'],
      }),
    );
    expect(container.textContent).toContain('AGV の登録（加える・変える・そろえる・消す）は、まだ使えません。');

    link.respondToAgvTypes('inUse');
    const row = [...container.querySelectorAll('[aria-labelledby="type-title"] tbody tr')].find((r) => r.textContent?.includes('小さい AGV'))!;
    click(buttonByText(row, '消す'));
    await act(async () => {
      click(buttonByText(container.querySelector('[role="alertdialog"]')!, '種類を消す'));
      await Promise.resolve();
    });

    expect(link.agvTypeCommands).toEqual([{ kind: 'delete', id: 'small' }]);
    expect(container.querySelector('[aria-labelledby="type-title"] [role="alert"]')?.textContent).toBe('その種類を使う AGV があるので、消せませんでした。');
    window.location.hash = '';
  });

  it('URL で指定した装置を、装置とストッカーの画面で開く', () => {
    window.location.hash = '#/devices/DEV-2';
    const link = connected();
    const { container } = render(<App link={link} />);
    expect(container.textContent).toContain('装置とステーションの様子を読み込んでいます。');

    act(() => link.emitEquipment(equipmentStatus));

    expect(container.querySelector<HTMLSelectElement>('select[aria-label="装置"]')?.value).toBe('DEV-2');
    expect(container.textContent).toContain('DEV-2（受け渡しパネル D2）');
    window.location.hash = '';
  });

  it('装置とストッカーの画面で、届いた装置の中を出し、設定の保存をサーバーへ送って、断られたら理由を出す', async () => {
    window.location.hash = '#/devices/DEV-1';
    const link = connected();
    const { container } = render(<App link={link} />);
    act(() => link.emitEquipment(equipmentStatus));
    expect(container.querySelector('[aria-labelledby="inside-title"]')?.textContent).toContain('T-1');

    link.respondToConsumables('exceedsSlots');
    click(buttonByText(container, '設定を変える（段取り替え）'));
    await act(async () => {
      click(buttonByText(container, '設定を保存する'));
      await Promise.resolve();
    });

    expect(link.consumableCommands).toEqual([{ kind: 'stockerSetting', device: 'DEV-1', counts: [{ variety: 'DR-5', count: 1 }] }]);
    expect(container.querySelector('[role="alert"]')?.textContent).toBe('個数の合計がスロットの数を超えているので、保存できませんでした。');
    window.location.hash = '';
  });

  it('ステーションの在庫の画面で、届いた消耗品を出し、出庫をサーバーへ送る', async () => {
    window.location.hash = '#/station';
    const link = connected();
    const { container } = render(<App link={link} />);
    act(() => link.emitEquipment(equipmentStatus));
    expect(container.textContent).toContain('届ける予約あり（DEV-1 へ）');

    link.respondToConsumables('inAssignedTransport');
    const row = [...container.querySelectorAll('[aria-labelledby="items-title"] tbody tr')].find((r) => r.textContent?.includes('T-5'))!;
    click(buttonByText(row, '出庫する'));
    await act(async () => {
      click(buttonByText(container.querySelector('[role="alertdialog"]')!, '出庫する'));
      await Promise.resolve();
    });

    expect(link.consumableCommands).toEqual([{ kind: 'ship', consumable: 'T-5' }]);
    expect(container.querySelector('[role="alert"]')?.textContent).toBe('届ける搬送に AGV を割り当て済みなので、出庫できませんでした。');
    window.location.hash = '';
  });

  it('要確認の消耗品を直すときは、要確認になったわけを出し、直した内容をサーバーへ送る', async () => {
    window.location.hash = '#/station';
    const link = connected();
    const { container } = render(<App link={link} />);
    act(() => link.emitEquipment(equipmentStatus));

    click(buttonByText(container, '確かめて直す'));
    const dialog = container.querySelector('[role="dialog"]')!;
    expect(dialog.textContent).toContain('装置で ID が食い違ったので、回収しました。');
    const select = dialog.querySelector<HTMLSelectElement>('select')!;
    expect([...select.options].map((o) => o.value)).toEqual(['', 'DR-5', 'DR-6']);
    act(() => {
      select.value = 'DR-6';
      select.dispatchEvent(new Event('change', { bubbles: true }));
    });
    click(dialog.querySelector<HTMLInputElement>('input[value="中古品"]')!);
    await act(async () => {
      click(buttonByText(dialog, '直して、要確認を外す'));
      await Promise.resolve();
    });

    expect(link.consumableCommands).toEqual([{ kind: 'correct', consumable: 'X-1', variety: 'DR-6', condition: '中古品' }]);
    window.location.hash = '';
  });

  it('マップの編集で保存していない変更があれば、ほかの画面へ移る前に確かめる', () => {
    window.location.hash = '#/mapEdit';
    const { container } = render(<App link={connected()} />);

    click(container.querySelector('[aria-label="置くもの"] button[data-tool="Normal"]')!);
    click(container.querySelector('[data-cell]')!);
    click(container.querySelector('button[aria-controls="main-menu"]')!);
    click(container.querySelector('[data-screen="transports"]')!);

    const dialog = container.querySelector('[role="alertdialog"]')!;
    expect(dialog.textContent).toContain('保存していない変更を捨てて移りますか？');
    click([...dialog.querySelectorAll('button')].find((b) => b.textContent === '変更を捨てて移る')!);
    expect(container.querySelector('h1')?.textContent).toBe('搬送の一覧');
    window.location.hash = '';
  });

  it.each([
    ['', false],
    ['#/devices', false],
    ['#/station', false],
    ['#/transports', false],
    ['#/agvs', false],
    ['#/users', true],
    ['#/consumables', false],
    ['#/consumableConfig', true],
  ])('見本のデータで動かしている画面だけ、そのことを帯で知らせる（%s）', (hash, demo) => {
    window.location.hash = hash;
    const { container } = render(<App link={connected()} />);

    expect(container.textContent?.includes('この画面は、見本のデータです')).toBe(demo);
    window.location.hash = '';
  });

  it('サーバーから届いた装置とステーションの様子で、マップの札とカードを出す', () => {
    window.location.hash = '';
    const link = connected();
    const { container } = render(<App link={link} />);
    const dev1 = () => container.querySelector('[data-equipment="DEV-1"]');
    expect(dev1()?.getAttribute('aria-label')).toBe('DEV-1'); // 様子が届くまでは、札がない

    act(() =>
      link.emitEquipment({
        version: 1,
        devices: [
          {
            id: 'DEV-1',
            panel: 'D1',
            shortage: 2,
            collectWaiting: 0,
            mismatchedSlots: [],
            setupErrors: [],
            stocker: [{ variety: 'DR-5', target: 3, have: 1 }],
            slots: { total: 10, free: 8, delivering: 0 },
            processing: [],
            stockerSlots: [],
            handover: [],
          },
        ],
        stations: [],
        varieties: ['DR-5'],
      }),
    );

    expect(dev1()?.getAttribute('aria-label')).toBe('DEV-1（不足 2）');
    click(dev1()!);
    const card = container.querySelector('[data-agv-card]');
    expect(card?.textContent).toContain('まだ 2個足りません');
    expect(card?.textContent).toContain('DR-5');
  });

  it('マップが届くまでは、読み込んでいることを出す。届いたら、サーバーのマップと装置とステーションを描き、版を出す', () => {
    window.location.hash = '';
    const link = createFakeLink({ connection: 'connected', status: { version: 1, state: 'started' } });
    const { container } = render(<App link={link} />);
    expect(container.textContent).toContain('マップを読み込んでいます');
    expect(container.querySelector('svg[aria-label="マップ"]')).toBeNull();

    act(() =>
      link.emitMap({
        version: 1,
        mapVersion: 7,
        layout: { panelSizeMm: 1000, panels: sampleLayout.panels.filter((p) => p.id !== 'SP'), connections: sampleLayout.connections.filter((c) => c.to !== 'SP') },
        equipment: [{ id: sampleEquipment[2]!.id, kind: 'device', panel: sampleEquipment[2]!.panel, outlineMm: sampleEquipment[2]!.outlineMm, attentions: [] }],
      }),
    );

    expect(container.querySelector('svg[aria-label="マップ"]')).not.toBeNull();
    expect(container.querySelectorAll('[data-equipment]')).toHaveLength(1);
    expect(container.querySelector('[data-panel="SP"]')).toBeNull();
    click(container.querySelector('button[aria-controls="main-menu"]')!);
    expect(container.textContent).toContain('マップ 版7');
  });

  // ---- AGV の様子 ----

  it('サーバーから届いた AGV を、マップの上と、位置がわからない列に描き、変わったら描き直す', () => {
    const link = connected();
    const { container } = render(<App link={link} />);
    expect(container.querySelector('[data-agv]')).toBeNull(); // 届くまでは、いない

    act(() =>
      link.emitAgvs({
        version: 1,
        agvs: [
          { id: 'AGV-1', panel: 'P3', heading: 90, state: 'moving', control: 'auto', carrierMoving: false, batteryPercent: 72, reservedPanels: [], route: [], transport: null },
          { id: 'AGV-2', panel: null, heading: null, state: 'unregistered', control: 'detached', carrierMoving: false, batteryPercent: null, reservedPanels: [], route: [], transport: null },
        ],
      }),
    );
    const agv1 = container.querySelector('[data-agv="AGV-1"]');
    expect(agv1?.hasAttribute('data-unplaced')).toBe(false);
    expect(agv1?.textContent).toBe('AGV-1'); // 印の名前は ID そのまま
    expect(container.querySelector('[data-agv="AGV-2"]')?.hasAttribute('data-unplaced')).toBe(true);

    act(() =>
      link.emitAgvs({
        version: 2,
        agvs: [{ id: 'AGV-1', panel: 'P4', heading: 90, state: 'idle', control: 'auto', carrierMoving: false, batteryPercent: 71, reservedPanels: [], route: [], transport: null }],
      }),
    );
    expect(container.querySelector('[data-agv="AGV-1"]')?.getAttribute('transform')).toContain('translate(4500 1500)'); // P4 の十字
    expect(container.querySelector('[data-agv="AGV-2"]')).toBeNull();
  });

  it('予約しているパネルと走る予定の経路、割り当て済みの搬送を、サーバーから届いた様子で出す', () => {
    const link = createFakeLink({
      connection: 'connected',
      map: serverMap,
      status: { version: 1, state: 'started' },
      agvs: {
        version: 1,
        agvs: [
          {
            id: 'AGV-1', panel: 'P3', heading: 90, state: 'moving', control: 'auto', carrierMoving: false, batteryPercent: 72,
            reservedPanels: ['P3', 'P4'], route: ['P3', 'P4', 'P5'],
            transport: { id: 12, from: 'ST-1', to: 'DEV-1', stage: 'movingToDestination', paused: false },
          },
        ],
      },
    });
    const { container } = render(<App link={link} />);

    expect(container.querySelectorAll('[data-reserved]')).toHaveLength(2);
    expect(container.querySelectorAll('[data-route]')).toHaveLength(2); // P3→P4、P4→P5
    click(container.querySelector('[data-agv="AGV-1"]')!);
    const card = container.querySelector('[data-agv-card]');
    expect(card?.textContent).toContain('#12 ST-1 から DEV-1 へ');
    expect(card?.textContent).toContain('搬送先へ移動');
  });

  it('位置がわからない AGV を押すと、カードを開き、位置を「わからない」と出す', () => {
    const link = createFakeLink({
      connection: 'connected',
      map: serverMap,
      status: { version: 1, state: 'started' },
      agvs: { version: 1, agvs: [{ id: 'AGV-2', panel: null, heading: null, state: 'unregistered', control: 'detached', carrierMoving: false, batteryPercent: null, reservedPanels: [], route: [], transport: null }] },
    });
    const { container } = render(<App link={link} />);

    click(container.querySelector('[data-agv="AGV-2"]')!);

    const card = container.querySelector('[data-agv-card]');
    expect(card?.textContent).toContain('位置がわからない');
    expect(card?.textContent).toContain('わからない');
  });

  // ---- サーバーとのつながり ----

  it('サーバーから届いた運転状態を出し、変わったら差し替える', () => {
    const link = connected();
    const { container } = render(<App link={link} />);
    expect(headerStatus(container)).toBe('運転中');

    act(() => link.emitSystemStatus({ version: 2, state: 'stopped' }));

    expect(headerStatus(container)).toBe('停止中');
  });

  it('運転状態が届くまでは、確かめていることを出し、切り替えを押せない', () => {
    const { container } = render(<App link={createFakeLink({ connection: 'connected', map: serverMap })} />);

    expect(headerStatus(container)).toBe('運転状態を確かめています');
    expect(buttonByText(runSwitch(container), '停止').disabled).toBe(true);
  });

  it('つながりが切れたら、帯で知らせ、運転の切り替えを押せなくする。つながったら戻す', () => {
    const link = connected();
    const { container } = render(<App link={link} />);

    act(() => link.emitConnection('reconnecting'));
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('サーバーとつながっていません');
    expect(headerStatus(container)).toBe('運転中'); // 切れる前の様子を出したまま
    expect(buttonByText(runSwitch(container), '停止').disabled).toBe(true);

    act(() => {
      link.emitConnection('connected');
      link.emitSystemStatus({ version: 1, state: 'started' });
    });
    expect(container.textContent).not.toContain('サーバーとつながっていません');
    expect(buttonByText(runSwitch(container), '停止').disabled).toBe(false);
  });

  it('運転の切り替えはサーバーへ送り、表示は届いた様子で変える', async () => {
    const link = connected();
    const { container } = render(<App link={link} />);

    await stopFromSwitch(container);

    expect(link.sent).toEqual(['stop']);
    expect(headerStatus(container)).toBe('運転中'); // 送っただけでは変えない
    act(() => link.emitSystemStatus({ version: 2, state: 'stopped' }));
    expect(headerStatus(container)).toBe('停止中');
  });

  it('送って結果が返るまでは、切り替えを押せない（連打を防ぐ）', async () => {
    const link = connected();
    link.holdResponses();
    const { container } = render(<App link={link} />);

    await stopFromSwitch(container);
    expect(buttonByText(runSwitch(container), 'スタート').disabled).toBe(true);

    await act(async () => {
      link.release();
      await Promise.resolve();
    });
    expect(buttonByText(runSwitch(container), 'スタート').disabled).toBe(false);
  });

  it('送れなかったら（応答がない・断られた）、メッセージで知らせる', async () => {
    const link = connected();
    link.respondWith('failed');
    const { container } = render(<App link={link} />);

    await stopFromSwitch(container);

    expect(container.textContent).toContain('停止できたかわかりません');
    expect(link.sent).toEqual(['stop']); // やり直さない
  });

  it('サーバーから届いたメッセージを出し、まだ見ていない件数をタブに出す。見たことにすると、ブラウザーに覚える', () => {
    window.localStorage.clear();
    window.location.hash = '#/transports';
    const link = connected();
    const { container, rerender } = render(<App link={link} />);
    const tab = () => container.querySelector('button[aria-controls="message-rail"]')!;

    act(() =>
      link.emitMessages({
        version: 1,
        messages: [
          { sequence: 2, at: new Date(), title: '行ける充電ポートがありません', detail: 'AGV-2 は、充電ポートが空いたら向かいます。' },
          { sequence: 1, at: new Date(), title: '搬送を中断しました', detail: '搬送 #11' },
        ],
      }),
    );
    expect(tab().textContent).toContain('2'); // 初めて開いたブラウザーでは、すべて「まだ見ていない」

    click(tab());
    expect(container.querySelector('#message-rail')?.textContent).toContain('行ける充電ポートがありません');
    click(buttonByText(container.querySelector('#message-rail')!, 'すべて見たことにする'));
    click(tab());
    expect(tab().getAttribute('aria-label')).toContain('0件');

    // 開き直しても、見たところを覚えている。新しく届いたものだけ「まだ見ていない」
    rerender(<App key="again" link={link} />);
    act(() =>
      link.emitMessages({
        version: 2,
        messages: [{ sequence: 3, at: new Date(), title: '予約が取れずに止まっています', detail: '' }, { sequence: 2, at: new Date(), title: '行ける充電ポートがありません', detail: '' }],
      }),
    );
    expect(container.querySelector('button[aria-controls="message-rail"]')!.getAttribute('aria-label')).toContain('1件');
    window.location.hash = '';
  });

  it('画面を閉じたら、サーバーとのつながりを切る', () => {
    const link = connected();
    const { rerender } = render(<App link={link} />);

    rerender(null);

    expect(link.connected).toBe(false);
  });

  // ---- AGV への操作 ----

  /** AGV-1 が P3 にいて、AGV-1 のカードを開いた形。 */
  function openAgv1(control: 'auto' | 'manual' = 'auto') {
    const link = createFakeLink({
      connection: 'connected',
      map: serverMap,
      status: { version: 1, state: 'started' },
      agvs: { version: 1, agvs: [{ id: 'AGV-1', panel: 'P3', heading: 90, state: 'idle', control, carrierMoving: false, batteryPercent: 72, reservedPanels: [], route: [], transport: null }] },
    });
    const rendered = render(<App link={link} />);
    click(rendered.container.querySelector('[data-agv="AGV-1"]')!);
    const card = () => rendered.container.querySelector<HTMLElement>('[data-agv-card]')!;
    return { link, card, ...rendered };
  }

  const flush = () => act(async () => { await Promise.resolve(); await Promise.resolve(); });

  it('緊急停止は、確かめずにすぐサーバーへ送る', async () => {
    const { link, card } = openAgv1();

    click(buttonByText(card(), '緊急停止'));
    await flush();

    expect(link.agvCommands).toEqual([{ id: 'AGV-1', command: { kind: 'emergencyStop' } }]);
  });

  it('アラームクリアは、確かめてから送る', async () => {
    const { link, card, container } = openAgv1();

    click(buttonByText(card(), 'アラームクリア'));
    expect(link.agvCommands).toEqual([]);
    click(buttonByText(container.querySelector('[role="alertdialog"]')!, 'アラームクリアする'));
    await flush();

    expect(link.agvCommands).toEqual([{ id: 'AGV-1', command: { kind: 'alarmClear' } }]);
  });

  it('受け付けられなかったときは、カードの中に理由を出す', async () => {
    const { link, card } = openAgv1();
    link.respondToAgv('refused');

    click(buttonByText(card(), '緊急停止'));
    await flush();

    expect(card().querySelector('[data-agv-notice]')?.textContent).toContain('今の AGV の状態では、受け付けられませんでした');
  });

  it('送って結果が返るまでは、緊急停止のほかの操作を押せない', async () => {
    const { link, card } = openAgv1();
    link.holdResponses();

    click(buttonByText(card(), '割り当て停止'));
    await flush();
    expect(buttonByText(card(), 'アラームクリア').disabled).toBe(true);
    expect(buttonByText(card(), '緊急停止').disabled).toBe(false);

    await act(async () => {
      link.release();
      await Promise.resolve();
    });
    await flush();
    expect(buttonByText(card(), 'アラームクリア').disabled).toBe(false);
  });

  it('つながりが切れたら、緊急停止も押せない', () => {
    const { link, card } = openAgv1();

    act(() => link.emitConnection('reconnecting'));

    expect(buttonByText(card(), '緊急停止').disabled).toBe(true);
  });

  it('マニュアルの行き先を、マップのパネルから選んで送る', async () => {
    const { link, card } = openAgv1('manual');

    await act(async () => {
      click(buttonByText(card(), 'この行き先へ動かす'));
      await Promise.resolve();
    });

    expect(link.agvCommands).toEqual([{ id: 'AGV-1', command: { kind: 'manualMove', destination: 'D1', arrival: null } }]); // ID の順で最初のパネル
  });
});
