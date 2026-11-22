import { describe, expect, it } from 'vitest';
import { initialRoles } from './permissions';
import { cancelBlockedReason, initialTransportTab, retryBlockedReason, transportsOf, type Transport } from './transports';

const at = (hhmm: string) => new Date(`2026-11-23T${hhmm}:00`);

function transport(number: number, overrides: Partial<Transport> = {}): Transport {
  return {
    number,
    status: { stage: 'queued' },
    kind: '届ける',
    from: 'ST-1',
    to: 'DEV-1',
    cargo: '[品種A] 1個',
    neededBy: at('10:00'),
    requestedAt: at('09:00'),
    agv: null,
    step: '割り当て待ち',
    stepNote: '',
    ...overrides,
  } as Transport;
}

const suspended = (positionResetDone: boolean) => ({ stage: 'suspended', positionResetDone }) as const;

describe('transportsOf（タブごとの搬送と並べ方）', () => {
  it('選んだタブの搬送だけを、必要時刻が早い順に並べる', () => {
    const list = [
      transport(1, { neededBy: at('10:30') }),
      transport(2, { neededBy: at('10:10') }),
      transport(3, { status: { stage: 'running' }, neededBy: at('09:00') }),
    ];

    expect(transportsOf(list, 'queued').map((t) => t.number)).toEqual([2, 1]);
  });

  it('待ち行列で必要時刻が同じなら、頼まれた順（割り当てる順）', () => {
    const list = [transport(5, { requestedAt: at('09:30') }), transport(6, { requestedAt: at('09:10') })];

    expect(transportsOf(list, 'queued').map((t) => t.number)).toEqual([6, 5]);
  });

  it('ほかのタブで必要時刻が同じなら、搬送の番号の順', () => {
    const running = { status: { stage: 'running' } } as const;
    const list = [transport(12, { ...running, requestedAt: at('09:00') }), transport(9, { ...running, requestedAt: at('09:30') })];

    expect(transportsOf(list, 'running').map((t) => t.number)).toEqual([9, 12]);
  });
});

describe('initialTransportTab（開いたときのタブ）', () => {
  it('中断した搬送があれば「中断した」', () => {
    expect(initialTransportTab([transport(1), transport(2, { status: suspended(false) })])).toBe('suspended');
  });

  it('なければ「走っている」', () => {
    expect(initialTransportTab([transport(1)])).toBe('running');
    expect(initialTransportTab([])).toBe('running');
  });
});

describe('押せない理由', () => {
  it('パーミッションがなければ、取りやめもやり直しも押せない', () => {
    const t = transport(1, { status: suspended(true) });

    expect(cancelBlockedReason(initialRoles.閲覧のみ)).not.toBeNull();
    expect(retryBlockedReason(t, initialRoles.閲覧のみ)).not.toBeNull();
  });

  it('やり直すは、位置リセットが済むまで押せない', () => {
    expect(retryBlockedReason(transport(1, { status: suspended(false) }), initialRoles.運用)).toContain('位置リセット');
    expect(retryBlockedReason(transport(1, { status: suspended(true) }), initialRoles.運用)).toBeNull();
  });

  it('運用なら取りやめられる', () => {
    expect(cancelBlockedReason(initialRoles.運用)).toBeNull();
  });
});
