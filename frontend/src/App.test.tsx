import { act } from 'react';
import { describe, expect, it } from 'vitest';
import { click, render } from './test/render';
import { App } from './App';
import { createFakeLink, type FakeLink } from './server/fakeLink';

/** つながって、運転中の様子が届いた偽物のつながり。 */
const connected = (): FakeLink => createFakeLink({ connection: 'connected', status: { version: 1, state: 'started' } });

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

  it('URL で指定した装置を、装置とストッカーの画面で開く', () => {
    window.location.hash = '#/devices/DEV-2';
    const { container } = render(<App link={connected()} />);

    expect(container.querySelector<HTMLSelectElement>('select[aria-label="装置"]')?.value).toBe('DEV-2');
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

  it('運転状態と AGV のほかは、見本のデータで動かしていることを知らせる', () => {
    const { container } = render(<App link={connected()} />);

    expect(container.textContent).toContain('運転状態と AGV のほかは、見本のデータです');
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
    const { container } = render(<App link={createFakeLink({ connection: 'connected' })} />);

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
