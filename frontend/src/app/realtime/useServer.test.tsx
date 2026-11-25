import { act } from 'react';
import { describe, expect, it } from 'vitest';
import { render } from '../../testing/render';
import { createFakeLink } from '../../testing/fakeLink';
import { useServer, type ServerView } from './useServer';
import type { ServerLink } from './link';

function Probe({ link, onView }: { readonly link: ServerLink; readonly onView: (view: ServerView) => void }) {
  onView(useServer(link));
  return null;
}

function watch(link: ServerLink) {
  const views: ServerView[] = [];
  const rendered = render(<Probe link={link} onView={(v) => views.push(v)} />);
  return { latest: () => views[views.length - 1]!, rendered };
}

describe('useServer', () => {
  it('描いたらつなぎ、つながるまでは「つないでいる」で、運転状態はわからない', () => {
    const link = createFakeLink();
    const { latest } = watch(link);

    expect(link.connected).toBe(true);
    expect(latest()).toEqual({ connection: 'connecting', status: { kind: 'waiting' }, agvs: { kind: 'waiting' }, messages: { kind: 'waiting' }, map: { kind: 'waiting' }, equipment: { kind: 'waiting' } });
  });

  it('届いた運転状態の様子と、つながりの様子を持つ', () => {
    const link = createFakeLink();
    const { latest } = watch(link);

    act(() => {
      link.emitConnection('connected');
      link.emitSystemStatus({ version: 1, state: 'started' });
    });

    expect(latest()).toMatchObject({ connection: 'connected', status: { kind: 'live', snapshot: { version: 1, state: 'started' } } });
  });

  it('古い番号の様子は捨てる', () => {
    const link = createFakeLink();
    const { latest } = watch(link);

    act(() => {
      link.emitSystemStatus({ version: 3, state: 'stopped' });
      link.emitSystemStatus({ version: 2, state: 'started' });
    });

    expect(latest().status).toEqual({ kind: 'live', snapshot: { version: 3, state: 'stopped' } });
  });

  it('切れたら、最後の様子を古いものにし、つなぎ直したら届いた様子を番号にかかわらず使う', () => {
    const link = createFakeLink();
    const { latest } = watch(link);
    act(() => {
      link.emitConnection('connected');
      link.emitSystemStatus({ version: 7, state: 'started' });
    });

    act(() => link.emitConnection('reconnecting'));
    expect(latest()).toMatchObject({ connection: 'reconnecting', status: { kind: 'stale', snapshot: { version: 7, state: 'started' } } });

    act(() => {
      link.emitConnection('connected');
      link.emitSystemStatus({ version: 1, state: 'stopped' }); // サーバーが起動し直した
    });
    expect(latest().status).toEqual({ kind: 'live', snapshot: { version: 1, state: 'stopped' } });
  });

  it('AGV の様子も、番号の古いものは捨て、切れたら古いものにする', () => {
    const link = createFakeLink();
    const { latest } = watch(link);
    const agv = { id: 'AGV-1', panel: 'P3', heading: 90, state: 'idle', control: 'auto', carrierMoving: false, batteryPercent: 50, reservedPanels: [], route: [], transport: null } as const;

    act(() => {
      link.emitAgvs({ version: 2, agvs: [agv] });
      link.emitAgvs({ version: 1, agvs: [] });
    });
    expect(latest().agvs).toEqual({ kind: 'live', snapshot: { version: 2, agvs: [agv] } });

    act(() => link.emitConnection('reconnecting'));
    expect(latest().agvs).toEqual({ kind: 'stale', snapshot: { version: 2, agvs: [agv] } });
  });

  it('メッセージの様子も、番号の古いものは捨て、切れたら古いものにする', () => {
    const link = createFakeLink();
    const { latest } = watch(link);
    const message = { sequence: 1, at: new Date('2026-11-24T01:00:00Z'), title: '搬送を中断しました', detail: '' };

    act(() => {
      link.emitMessages({ version: 2, messages: [message] });
      link.emitMessages({ version: 1, messages: [] });
    });
    expect(latest().messages).toEqual({ kind: 'live', snapshot: { version: 2, messages: [message] } });

    act(() => link.emitConnection('reconnecting'));
    expect(latest().messages).toEqual({ kind: 'stale', snapshot: { version: 2, messages: [message] } });
  });

  it('マップの様子も、番号の古いものは捨て、切れたら古いものにする', () => {
    const link = createFakeLink();
    const { latest } = watch(link);
    const map = { version: 2, mapVersion: 1, layout: { panelSizeMm: 1000, panels: [], connections: [] }, equipment: [] };

    act(() => {
      link.emitMap(map);
      link.emitMap({ ...map, version: 1 });
    });
    expect(latest().map).toEqual({ kind: 'live', snapshot: map });

    act(() => link.emitConnection('reconnecting'));
    expect(latest().map).toEqual({ kind: 'stale', snapshot: map });
  });

  it('装置とステーションの様子も、番号の古いものは捨て、切れたら古いものにする', () => {
    const link = createFakeLink();
    const { latest } = watch(link);
    const equipment = { version: 2, devices: [], stations: [{ id: 'ST-1', panel: 'ST', needsCheck: 1, replenishWaiting: 0, stock: [], items: [] }], varieties: [] };

    act(() => {
      link.emitEquipment(equipment);
      link.emitEquipment({ ...equipment, version: 1, stations: [] });
    });
    expect(latest().equipment).toEqual({ kind: 'live', snapshot: equipment });

    act(() => link.emitConnection('reconnecting'));
    expect(latest().equipment).toEqual({ kind: 'stale', snapshot: equipment });
  });

  it('消したら切る', () => {
    const link = createFakeLink();
    const { rendered } = watch(link);

    rendered.rerender(null);

    expect(link.connected).toBe(false);
  });
});
