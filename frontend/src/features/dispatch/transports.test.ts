import { describe, expect, it } from 'vitest';
import { initialRoles } from '../auth/permissions';
import { cancelBlockedReason, initialTransportTab, permissionNote, retryBlockedReason, transportsOf, type Transport } from './transports';

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
    agv: null,
    step: '割り当て待ち',
    stepNote: '',
    ...overrides,
  };
}

const suspended = { stage: 'suspended' } as const;

describe('transportsOf（タブごとの搬送と並べ方）', () => {
  it('選んだタブの搬送だけを、必要時刻が早い順に並べる', () => {
    const list = [
      transport(1, { neededBy: at('10:30') }),
      transport(2, { neededBy: at('10:10') }),
      transport(3, { status: { stage: 'running' }, neededBy: at('09:00') }),
    ];

    expect(transportsOf(list, 'queued').map((t) => t.number)).toEqual([2, 1]);
  });

  it('必要時刻が同じなら、搬送の番号の順（番号は頼まれた順に付くので、待ち行列では割り当てる順）', () => {
    expect(transportsOf([transport(6), transport(5)], 'queued').map((t) => t.number)).toEqual([5, 6]);

    const running = { status: { stage: 'running' } } as const;
    expect(transportsOf([transport(12, running), transport(9, running)], 'running').map((t) => t.number)).toEqual([9, 12]);
  });
});

describe('initialTransportTab（開いたときのタブ）', () => {
  it('中断した搬送があれば「中断した」', () => {
    expect(initialTransportTab([transport(1), transport(2, { status: suspended })])).toBe('suspended');
  });

  it('なければ「走っている」', () => {
    expect(initialTransportTab([transport(1)])).toBe('running');
    expect(initialTransportTab([])).toBe('running');
  });
});

describe('押せない理由', () => {
  it('パーミッションがなければ、取りやめもやり直しも押せない', () => {
    expect(cancelBlockedReason(transport(1), initialRoles.閲覧のみ, false)).toContain('運用以上のロール');
    expect(retryBlockedReason(initialRoles.閲覧のみ)).toContain('運用以上のロール');
    expect(permissionNote(initialRoles.閲覧のみ)).toContain('運用以上のロール');
    expect(permissionNote(initialRoles.運用)).toBeNull();
  });

  it('中断した搬送は、やり直しも取りやめもまだできない', () => {
    expect(cancelBlockedReason(transport(1, { status: suspended }), initialRoles.運用, false)).toContain('まだできません');
    expect(retryBlockedReason(initialRoles.運用)).toContain('まだできません');
  });

  it('サーバーとつながっていなければ、取りやめられない', () => {
    expect(cancelBlockedReason(transport(1), initialRoles.運用, true)).toContain('サーバーとつながっていない');
  });

  it('運用なら、待ち行列と走っている搬送を取りやめられる', () => {
    expect(cancelBlockedReason(transport(1), initialRoles.運用, false)).toBeNull();
    expect(cancelBlockedReason(transport(1, { status: { stage: 'running' } }), initialRoles.運用, false)).toBeNull();
  });
});
