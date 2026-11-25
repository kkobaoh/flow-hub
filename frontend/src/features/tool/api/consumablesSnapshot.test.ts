import { describe, expect, it } from 'vitest';
import { parseConsumables, toHit, toRecord, type ConsumableData } from './consumablesSnapshot';

const wire = {
  id: 'T-1',
  variety: 'DR-5',
  processType: 'drill',
  condition: 'new',
  location: { kind: 'station', station: 'ST-1', device: null, stocker: null, slot: null, agv: null },
  checkReason: null,
  reservation: { kind: 'delivery', device: 'DEV-1', transport: 3, assigned: false, from: 'ST-1', to: 'DEV-1' },
  designation: { device: 'DEV-1', assigned: false },
  shipping: false,
};

describe('parseConsumables', () => {
  it('サーバーから届いた消耗品の記録を、型にする', () => {
    expect(parseConsumables({ version: 2, consumables: [wire] })).toEqual({
      version: 2,
      consumables: [
        {
          id: 'T-1',
          variety: 'DR-5',
          processType: 'drill',
          condition: 'new',
          location: { kind: 'station', station: 'ST-1' },
          checkReason: null,
          reservation: { kind: 'delivery', device: 'DEV-1', transport: 3, assigned: false },
          designation: { device: 'DEV-1', assigned: false },
          shipping: false,
        },
      ],
    });
  });

  it.each([
    ['並びがない', { version: 1 }],
    ['ID がない', { version: 1, consumables: [{ ...wire, id: '' }] }],
    ['知らない所在', { version: 1, consumables: [{ ...wire, location: { kind: 'moon' } }] }],
    ['スロットの番号がない', { version: 1, consumables: [{ ...wire, location: { kind: 'slot', device: 'DEV-1', stocker: 1, slot: null } }] }],
    ['知らない予約', { version: 1, consumables: [{ ...wire, reservation: { ...wire.reservation, kind: 'gift' } }] }],
    ['知らない要確認のわけ', { version: 1, consumables: [{ ...wire, checkReason: 'why' }] }],
  ])('形が正しくなければ、例外にする（%s）', (_, value) => {
    expect(() => parseConsumables(value)).toThrow();
  });
});

const data = (more: Partial<ConsumableData>): ConsumableData => ({
  id: 'T-1',
  variety: 'DR-5',
  processType: 'drill',
  condition: 'new',
  location: { kind: 'station', station: 'ST-1' },
  checkReason: null,
  reservation: null,
  designation: null,
  shipping: false,
  ...more,
});

describe('toRecord（消耗品の検索の画面に出す形にする）', () => {
  it('所在、予約、指名を画面の形にする', () => {
    expect(toRecord(data({ reservation: { kind: 'delivery', device: 'DEV-1', transport: 3, assigned: true }, designation: { device: 'DEV-1', assigned: true } }))).toEqual({
      id: 'T-1',
      variety: 'DR-5',
      processKind: 'drill',
      condition: '新品',
      location: { kind: 'station', station: 'ST-1' },
      needsCheck: false,
      reservation: { transport: 3, assigned: true, purpose: 'DEV-1 へ届ける' },
      nomination: { device: 'DEV-1', assigned: true },
      shipping: false,
    });
  });

  it('回収の予約と、要確認と、品種のわからないもの', () => {
    const r = toRecord(data({ variety: null, processType: null, condition: null, checkReason: 'idMismatch', reservation: { kind: 'recovery', device: null, transport: null, assigned: false } }));

    expect(r).toMatchObject({ variety: null, processKind: null, condition: null, needsCheck: true, reservation: { transport: null, assigned: false, purpose: '装置から回収する' } });
  });
});

describe('toHit（マップの「消耗品を探す」に出す形にする）', () => {
  it.each([
    [data({}), { kind: 'equipment', id: 'ST-1' }, 'ST-1', '予約なし'],
    [data({ location: { kind: 'slot', device: 'DEV-1', stocker: 1, slot: 2 } }), { kind: 'equipment', id: 'DEV-1' }, 'DEV-1 のストッカー1・スロット2', '予約なし'],
    [data({ location: { kind: 'agv', agv: 'AGV-1' }, reservation: { kind: 'delivery', device: 'DEV-1', transport: 3, assigned: true } }), { kind: 'agv', id: 'AGV-1' }, 'AGV-1 の中', '届ける予約あり（搬送 #3、DEV-1 へ届ける）'],
    [data({ location: { kind: 'unknown' }, checkReason: 'foundAfterLost' }), null, '所在不明', '要確認'],
    [data({ shipping: true }), { kind: 'equipment', id: 'ST-1' }, 'ST-1', '出庫を頼んだ'],
    [data({ designation: { device: 'DEV-2', assigned: false } }), { kind: 'equipment', id: 'ST-1' }, 'ST-1', '指名あり（DEV-2）'],
  ] as const)('ある場所と、状態の文（%#）', (d, at, where, status) => {
    expect(toHit(d)).toMatchObject({ id: 'T-1', where, status, at });
  });

  it('品種と新品・中古品がわからなければ「不明」', () => {
    expect(toHit(data({ variety: null, condition: null }))).toMatchObject({ variety: '不明', condition: '不明' });
  });
});
