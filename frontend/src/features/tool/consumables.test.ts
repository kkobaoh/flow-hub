import { describe, expect, it } from 'vitest';
import { equipmentId } from './equipment';
import { panelId } from '../routing/model';
import {
  cancelNominationBlockedReason,
  draftOfRecord,
  editBlockedReason,
  findConsumables,
  locationKey,
  locationLabel,
  locationOptions,
  nominateBlockedReason,
  recordProblem,
  type ConsumableRecord,
} from './consumables';
import type { DeviceInside } from './devices';
import { initialRoles } from '../auth/permissions';

function record(overrides: Partial<ConsumableRecord> = {}): ConsumableRecord {
  return {
    id: 'T-0101',
    variety: '[品種A]',
    processKind: '[加工の種類A]',
    condition: '新品',
    location: { kind: 'station', station: 'ST-1' },
    needsCheck: false,
    reservation: null,
    nomination: null,
    shipping: false,
    ...overrides,
  };
}

const 保守 = initialRoles.保守;
const 運用 = initialRoles.運用;

describe('locationLabel', () => {
  it('所在を文にする', () => {
    expect(locationLabel({ kind: 'station', station: 'ST-1' })).toBe('ST-1');
    expect(locationLabel({ kind: 'slot', device: 'DEV-1', stocker: 1, slot: 2 })).toBe('DEV-1 のストッカー1・スロット2');
    expect(locationLabel({ kind: 'plate', device: 'DEV-3', stocker: 1 })).toBe('DEV-3 のストッカー1');
    expect(locationLabel({ kind: 'processing', device: 'DEV-1' })).toBe('DEV-1 の加工部');
    expect(locationLabel({ kind: 'handover', device: 'DEV-1' })).toBe('DEV-1 の受け渡し部');
    expect(locationLabel({ kind: 'agv', agv: 'AGV-1' })).toBe('AGV-1 の中');
    expect(locationLabel({ kind: 'agv', agv: null })).toBe('AGV の中');
    expect(locationLabel({ kind: 'unknown' })).toBe('所在不明');
  });
});

describe('findConsumables（ID で探す）', () => {
  const records = [record({ id: 'T-0110' }), record({ id: 'T-0101' }), record({ id: 'X-9001' })];

  it('一部でも合うものを、ID の順に並べる', () => {
    expect(findConsumables(records, 't01').map((r) => r.id)).toEqual(['T-0101', 'T-0110']);
  });

  it('何も入れていなければ、何も出さない', () => {
    expect(findConsumables(records, ' ')).toEqual([]);
  });
});

describe('editBlockedReason（記録を直す・消すのを押せない理由）', () => {
  it('保守以上のロールでなければ、使えない', () => {
    expect(editBlockedReason(record(), 運用)).toBe('記録を直す・消すのは、保守以上のロールで使えます。');
  });

  it('AGV の中、加工部・受け渡し部、AGV を割り当て済みの搬送の予約付き、出庫を頼んだものは、直せない', () => {
    expect(editBlockedReason(record({ location: { kind: 'agv', agv: 'AGV-1' } }), 保守)).toContain('AGV に積んでいる');
    expect(editBlockedReason(record({ location: { kind: 'processing', device: 'DEV-1' } }), 保守)).toContain('加工部');
    expect(editBlockedReason(record({ location: { kind: 'handover', device: 'DEV-1' } }), 保守)).toContain('受け渡し部');
    expect(editBlockedReason(record({ reservation: { transport: 12, assigned: true, purpose: '' } }), 保守)).toContain('搬送 #12');
    expect(editBlockedReason(record({ shipping: true }), 保守)).toContain('出庫を頼んでいる');
  });

  it('割り当て前の予約や指名だけなら、直せる', () => {
    expect(editBlockedReason(record({ reservation: { transport: 14, assigned: false, purpose: '' } }), 保守)).toBeNull();
    expect(editBlockedReason(record({ nomination: { device: 'DEV-1', assigned: false } }), 保守)).toBeNull();
  });
});

describe('nominateBlockedReason（指名を押せない理由）', () => {
  it('ステーションにある、要確認でない新品で、予約も出庫もないものだけ指名できる', () => {
    expect(nominateBlockedReason(record(), 運用)).toBeNull();
    for (const r of [
      record({ location: { kind: 'slot', device: 'DEV-1', stocker: 1, slot: 1 } }),
      record({ needsCheck: true }),
      record({ condition: '中古品' }),
      record({ reservation: { transport: 14, assigned: false, purpose: '' } }),
      record({ shipping: true }),
    ]) {
      expect(nominateBlockedReason(r, 運用)).toBe('指名できるのは、ステーションにある、要確認でない新品で、届ける予約も出庫の頼みもないものです。');
    }
  });

  it('パーミッションがなければ、指名できない', () => {
    expect(nominateBlockedReason(record(), initialRoles.閲覧のみ)).toBe('指名と AGV での交換は、運用以上のロールで使えます。');
  });
});

describe('cancelNominationBlockedReason（指名の取り消し）', () => {
  it('割り当て前なら取り消せる', () => {
    expect(cancelNominationBlockedReason(record({ nomination: { device: 'DEV-1', assigned: false } }), 運用)).toBeNull();
    expect(cancelNominationBlockedReason(record({ nomination: { device: 'DEV-1', assigned: true } }), 運用)).toBe(
      '届ける搬送に AGV を割り当て済みなので、指名を取り消せません。',
    );
  });
});

const dev1: DeviceInside = {
  id: equipmentId('DEV-1'),
  panel: panelId('D1'),
  processing: [],
  stockers: [
    {
      name: 'ストッカー1',
      layout: 'slots',
      slots: [
        { number: 1, content: { kind: 'held', consumable: { id: 'T-0101', variety: '[品種A]', note: '', tone: 'normal' } } },
        { number: 2, content: { kind: 'empty' } },
      ],
    },
  ],
  handover: [],
  setting: [],
};
const dev3: DeviceInside = { ...dev1, id: equipmentId('DEV-3'), stockers: [{ name: 'ストッカー1', layout: 'plate', items: [] }] };

describe('locationOptions（選べる所在）', () => {
  it('ステーションと、装置のストッカーの空きスロット（プレートはストッカー）', () => {
    expect(locationOptions(['ST-1'], [dev3, dev1], null).map(locationLabel)).toEqual(['ST-1', 'DEV-1 のストッカー1・スロット2', 'DEV-3 のストッカー1']);
  });

  it('直すときは、いまの所在も選べる', () => {
    const current = { kind: 'slot', device: 'DEV-1', stocker: 1, slot: 1 } as const;
    expect(locationOptions(['ST-1'], [dev1], current).map(locationLabel)).toContain('DEV-1 のストッカー1・スロット1');
  });
});

describe('recordProblem（保存できない理由）', () => {
  const full = { id: 'T-0500', variety: '[品種A]', condition: '新品', location: 'station:ST-1' } as const;

  it('登録は、すべて入れるまで保存できず、登録済みの ID は使えない', () => {
    expect(recordProblem({ ...full, id: ' ' }, { mode: 'register', existingIds: [] })).toBe('ID を入れてください。');
    expect(recordProblem({ ...full, variety: '' }, { mode: 'register', existingIds: [] })).toBe('品種を選んでください。');
    expect(recordProblem({ ...full, condition: null }, { mode: 'register', existingIds: [] })).toBe('新品か中古品かを選んでください。');
    expect(recordProblem({ ...full, location: '' }, { mode: 'register', existingIds: [] })).toBe('所在を選んでください。');
    expect(recordProblem(full, { mode: 'register', existingIds: ['T-0500'] })).toBe('T-0500 は、もう登録してあります。');
    expect(recordProblem(full, { mode: 'register', existingIds: [] })).toBeNull();
  });

  it('直すときは、何も変えていなければ保存できない', () => {
    const r = record({ id: 'T-0500' });
    const draft = draftOfRecord(r);
    expect(recordProblem(draft, { mode: 'edit', original: draft })).toBe('変えたところがありません。');
    expect(recordProblem({ ...draft, condition: '中古品' }, { mode: 'edit', original: draft })).toBeNull();
  });

  it('直すときの下書きは、いまの記録を入れ、記録にない項目は未選択', () => {
    expect(draftOfRecord(record())).toEqual({ id: 'T-0101', variety: '[品種A]', condition: '新品', location: locationKey({ kind: 'station', station: 'ST-1' }) });
    expect(draftOfRecord(record({ variety: null, condition: null, location: { kind: 'unknown' } }))).toEqual({ id: 'T-0101', variety: '', condition: null, location: '' });
  });
});
