import { describe, expect, it } from 'vitest';
import { sampleLayout } from '../../../app/demo/sampleLayout';
import { mapOverlays, parseAgvs, toAgvDetails, type AgvWire } from './agvSnapshots';

const wire = (overrides: Partial<AgvWire> = {}): AgvWire => ({
  id: 'AGV-1',
  panel: 'P3',
  heading: 90,
  state: 'idle',
  control: 'auto',
  carrierMoving: false,
  batteryPercent: 72,
  reservedPanels: ['P3'],
  route: [],
  transport: null,
  ...overrides,
});

describe('parseAgvs', () => {
  it('サーバーから届いた AGV の様子を、型にする', () => {
    const value = { version: 4, agvs: [wire(), wire({ id: 'AGV-2', panel: null, heading: null, state: 'unregistered', batteryPercent: null })] };

    expect(parseAgvs(value)).toEqual(value);
  });

  it.each([
    ['並びがない', { version: 1 }],
    ['番号が正しくない', { version: 0, agvs: [] }],
    ['ID が空', { version: 1, agvs: [wire({ id: '' })] }],
    ['知らない状態', { version: 1, agvs: [wire({ state: 'sleeping' as never })] }],
    ['知らないコントロールステータス', { version: 1, agvs: [wire({ control: 'remote' as never })] }],
    ['向きが範囲の外', { version: 1, agvs: [wire({ heading: 360 })] }],
    ['パネルと向きの片方だけ', { version: 1, agvs: [wire({ heading: null })] }],
    ['充電残量が範囲の外', { version: 1, agvs: [wire({ batteryPercent: 101 })] }],
    ['予約が並びでない', { version: 1, agvs: [wire({ reservedPanels: 'P3' as never })] }],
    ['経路に空のパネル', { version: 1, agvs: [wire({ route: ['P3', ''] })] }],
    ['知らない搬送の段階', { version: 1, agvs: [wire({ transport: { id: 1, from: 'ST-1', to: 'DEV-1', stage: 'flying' as never, paused: false } })] }],
  ])('形が正しくなければ、例外にする（%s）', (_, value) => {
    expect(() => parseAgvs(value)).toThrow();
  });
});

describe('toAgvDetails', () => {
  it('マップにあるパネルにいれば、マップの上に置く。印の名前は ID そのまま', () => {
    const [agv] = toAgvDetails([wire()], sampleLayout);

    expect(agv).toMatchObject({ id: 'AGV-1', label: 'AGV-1', position: { kind: 'onMap', panel: 'P3', heading: 90 }, control: 'auto', batteryPercent: 72 });
  });

  it('位置が届いていなければ、位置がわからない', () => {
    const [agv] = toAgvDetails([wire({ panel: null, heading: null, state: 'unregistered' })], sampleLayout);

    expect(agv?.position).toEqual({ kind: 'unknown' });
    expect(agv?.operation).toBe('positionUnknown');
  });

  it('描いているマップにないパネルにいれば、マップの外にいるとして、届いたパネルを持つ', () => {
    const [agv] = toAgvDetails([wire({ panel: 'P99', heading: 180 })], sampleLayout);

    expect(agv?.position).toEqual({ kind: 'offMap', panel: 'P99', heading: 180 });
  });

  it.each([
    ['idle', 'idle', 'idle'],
    ['moving', 'moving', 'traveling'],
    ['emergencyStopped', 'idle', 'emergencyStopped'],
    ['error', 'idle', 'fault'],
    ['resettingPosition', 'idle', 'awaitingPositionReset'],
    ['unregistered', 'idle', 'positionUnknown'],
  ] as const)('状態 %s を、動き %s・画面の状態 %s にする', (state, activity, operation) => {
    const [agv] = toAgvDetails([wire({ state })], sampleLayout);

    expect(agv?.status.activity).toBe(activity);
    expect(agv?.operation).toBe(operation);
  });

  it('充電しているかは、AGV から知らせがないので、わからないにする。キャリアの上げ下げは届いたとおり', () => {
    const [agv] = toAgvDetails([wire({ carrierMoving: true })], sampleLayout);

    expect(agv?.status).toEqual({ activity: 'idle', charging: 'unknown', carrierMoving: true });
  });

  it('割り当て済みの搬送がなければ、なし', () => {
    expect(toAgvDetails([wire()], sampleLayout)[0]?.transport).toBeNull();
  });

  it('割り当て済みの搬送は、番号と搬送元から搬送先、今の段階にする', () => {
    const transport = { id: 12, from: 'ST-1', to: 'DEV-1', stage: 'movingToDestination', paused: false } as const;

    expect(toAgvDetails([wire({ transport })], sampleLayout)[0]?.transport).toEqual({ label: '#12 ST-1 から DEV-1 へ', step: '搬送先へ移動' });
  });

  it('一時停止している搬送は、段階に一時停止中と添える', () => {
    const transport = { id: 3, from: 'DEV-1', to: 'ST-1', stage: 'transferringAtSource', paused: true } as const;

    expect(toAgvDetails([wire({ transport })], sampleLayout)[0]?.transport?.step).toBe('搬送元で受け渡し中（一時停止中）');
  });
});

describe('mapOverlays（予約しているパネルと走る予定の経路）', () => {
  it('すべての AGV の予約しているパネルを、重ねずに集める（いるパネルも含む）', () => {
    const overlays = mapOverlays([wire({ reservedPanels: ['P3', 'P4'] }), wire({ id: 'AGV-2', reservedPanels: ['SB', 'P4'] })], sampleLayout);

    expect([...overlays.reservedPanels].sort()).toEqual(['P3', 'P4', 'SB']);
  });

  it('走る予定の経路は、AGV ごとに1本にする（走る予定がなければ描かない）', () => {
    const overlays = mapOverlays([wire({ route: ['P3', 'P4', 'P5'] }), wire({ id: 'AGV-2', route: [] })], sampleLayout);

    expect(overlays.routes).toEqual([['P3', 'P4', 'P5']]);
  });

  it('描いているマップにないパネルは、予約から外し、そのパネルを通る経路は描かない', () => {
    const overlays = mapOverlays([wire({ reservedPanels: ['P3', 'P99'], route: ['P3', 'P99'] })], sampleLayout);

    expect(overlays.reservedPanels).toEqual(['P3']);
    expect(overlays.routes).toEqual([]);
  });
});
