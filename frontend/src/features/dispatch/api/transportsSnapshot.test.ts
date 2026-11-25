import { describe, expect, it } from 'vitest';
import { parseTransports, toTransports, type TransportData } from './transportsSnapshot';

const wire = {
  id: 12,
  source: 'ST-1',
  destination: 'DEV-1',
  neededBy: '2026-11-25T10:00:00+00:00',
  purpose: 'delivery',
  cargo: [
    { variety: 'DR-5', count: 2 },
    { variety: null, count: 1 },
  ],
  stage: 'movingToDestination',
  agv: 'AGV-1',
  paused: false,
};

describe('parseTransports', () => {
  it('サーバーから届いた搬送の一覧を、型にする', () => {
    expect(parseTransports({ version: 3, transports: [wire] })).toEqual({
      version: 3,
      transports: [{ ...wire, neededBy: new Date('2026-11-25T10:00:00Z') }],
    });
  });

  it.each([
    ['並びがない', { version: 1 }],
    ['番号が 0', { version: 1, transports: [{ ...wire, id: 0 }] }],
    ['知らない目的', { version: 1, transports: [{ ...wire, purpose: 'play' }] }],
    ['知らない段階', { version: 1, transports: [{ ...wire, stage: 'flying' }] }],
    ['必要時刻が読めない', { version: 1, transports: [{ ...wire, neededBy: 'あした' }] }],
    ['数が 0', { version: 1, transports: [{ ...wire, cargo: [{ variety: 'DR-5', count: 0 }] }] }],
    ['割り当て待ちなのに AGV がある', { version: 1, transports: [{ ...wire, stage: 'waiting' }] }],
    ['割り当てたのに AGV がない', { version: 1, transports: [{ ...wire, agv: null }] }],
  ])('形が正しくなければ、例外にする（%s）', (_, value) => {
    expect(() => parseTransports(value)).toThrow();
  });
});

const data = (more: Partial<TransportData>): TransportData => ({
  id: 12,
  source: 'ST-1',
  destination: 'DEV-1',
  neededBy: new Date('2026-11-25T10:00:00Z'),
  purpose: 'delivery',
  cargo: [{ variety: 'DR-5', count: 2 }],
  stage: 'waiting',
  agv: null,
  paused: false,
  ...more,
});

describe('toTransports（搬送の一覧の画面に出す形にする）', () => {
  it('割り当て待ちは待ち行列、割り当てたものは走っている、中断したものは中断したにする', () => {
    const [queued, running, suspended] = toTransports([
      data({ id: 1 }),
      data({ id: 2, stage: 'movingToSource', agv: 'AGV-1' }),
      data({ id: 3, stage: 'interrupted', agv: 'AGV-2' }),
    ]);

    expect(queued).toMatchObject({ number: 1, status: { stage: 'queued' }, agv: null, step: '割り当て待ち' });
    expect(running).toMatchObject({ number: 2, status: { stage: 'running' }, agv: 'AGV-1', step: '搬送元へ移動' });
    expect(suspended).toMatchObject({ number: 3, status: { stage: 'suspended' }, agv: 'AGV-2', step: '中断' });
  });

  it('種類と運ぶものは、頼んだ側から聞いた目的と品種ごとの数で出す', () => {
    const [t] = toTransports([data({ cargo: [{ variety: 'DR-5', count: 2 }, { variety: null, count: 1 }] })]);

    expect(t).toMatchObject({ kind: '届ける', from: 'ST-1', to: 'DEV-1', cargo: 'DR-5 2個、不明な消耗品 1個' });
  });

  it.each([
    ['recovery', '回収する'],
    ['deliveryFromAgv', '届ける（AGV から）'],
    ['returnFromAgv', '返す（AGV から）'],
    [null, '－'],
  ] as const)('目的の言葉（%s）', (purpose, kind) => {
    expect(toTransports([data({ purpose })])[0]?.kind).toBe(kind);
  });

  it('目的がわからないものは、運ぶものの数だけ出す', () => {
    expect(toTransports([data({ purpose: null, cargo: [{ variety: null, count: 3 }] })])[0]?.cargo).toBe('3個');
  });

  it('一時停止中なら、段階の補足に出す', () => {
    expect(toTransports([data({ stage: 'movingToDestination', agv: 'AGV-1', paused: true })])[0]?.stepNote).toBe('一時停止中');
  });
});
