import { describe, expect, it } from 'vitest';
import { sampleLayout } from '../map/sampleLayout';
import { parseAgvs, toAgvDetails, type AgvWire } from './agvs';

const wire = (overrides: Partial<AgvWire> = {}): AgvWire => ({
  id: 'AGV-1',
  panel: 'P3',
  heading: 90,
  state: 'idle',
  control: 'auto',
  carrierMoving: false,
  batteryPercent: 72,
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

  it('割り当て済みの搬送は、まだサーバーから届かないので、なしにする', () => {
    expect(toAgvDetails([wire()], sampleLayout)[0]?.transport).toBeNull();
  });
});
