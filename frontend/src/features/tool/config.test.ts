import { describe, expect, it } from 'vitest';
import {
  deviceDeleteBlockedReason,
  deviceDraftOf,
  deviceProblem,
  idProblem,
  panelChoices,
  processKindDeleteBlockedReason,
  processKindProblem,
  removeIdBlockedReason,
  stationDeleteBlockedReason,
  stationAddBlockedReason,
  stationProblem,
  varietyDeleteBlockedReason,
  varietyProblem,
  type DeviceConfig,
  type DeviceDraft,
  type Variety,
} from './config';

const variety = (name: string, overrides: Partial<Variety> = {}): Variety => ({ name, processKind: '[加工A]', ids: [], stockedIds: [], inUse: false, ...overrides });

const dev1: DeviceConfig = { id: 'DEV-1', panel: 'D1', slots: [3, 3], occupied: [[1], [2, 3]], busy: true };
const empty: DeviceConfig = { id: 'DEV-9', panel: 'D9', slots: [2], occupied: [[]], busy: false };

const deviceDraft = (overrides: Partial<DeviceDraft> = {}): DeviceDraft => ({ id: 'DEV-5', panel: 'D5', slots: ['3'], ...overrides });

describe('processKindProblem（加工の種類を保存できない理由）', () => {
  const ok = { name: '[加工B]', carrier: '[キャリアA]', capacity: '3' } as const;

  it('名前・キャリアの種類・個数をすべて入れ、個数は 1 以上の整数', () => {
    expect(processKindProblem({ ...ok, name: '' }, [], null)).toBe('加工の種類の名前を入れてください。');
    expect(processKindProblem({ ...ok, carrier: ' ' }, [], null)).toBe('キャリアの種類を入れてください。');
    expect(processKindProblem({ ...ok, capacity: '0' }, [], null)).toBe('キャリアに入る個数を、1 以上の整数で入れてください。');
    expect(processKindProblem({ ...ok, name: '[加工A]' }, ['[加工A]'], null)).toBe('[加工A] は、もう登録してあります。');
    expect(processKindProblem(ok, ['[加工A]'], null)).toBeNull();
    expect(processKindProblem({ ...ok, name: '[加工A]' }, ['[加工A]'], '[加工A]')).toBeNull();
  });
});

describe('消すのを押せない理由', () => {
  it('加工の種類は、品種がなければ消せる', () => {
    expect(processKindDeleteBlockedReason('[加工A]', [variety('[品種B]'), variety('[品種A]')])).toBe('[品種A]、[品種B] の加工の種類なので、消せません。');
    expect(processKindDeleteBlockedReason('[加工Z]', [variety('[品種A]')])).toBeNull();
  });

  it('品種は、消耗品の記録やストッカーの設定に使われていなければ消せる', () => {
    expect(varietyDeleteBlockedReason(variety('[品種A]', { inUse: true }))).toBe('[品種A] は、消耗品の記録かストッカーの設定で使っているので、消せません。');
    expect(varietyDeleteBlockedReason(variety('[品種A]'))).toBeNull();
  });

  it('装置とステーションは、中に消耗品がなく、かかわる搬送もなければ消せる', () => {
    expect(deviceDeleteBlockedReason(dev1)).toBe('DEV-1 は、中に消耗品があるか、かかわる搬送があるので、消せません。');
    expect(deviceDeleteBlockedReason(empty)).toBeNull();
    expect(stationDeleteBlockedReason({ id: 'ST-1', panel: 'ST', busy: true })).not.toBeNull();
  });
});

describe('varietyProblem と ID の対応', () => {
  it('品種は、名前と加工の種類を入れ、同じ名前は使えない', () => {
    expect(varietyProblem({ name: '', processKind: '[加工A]' }, [], null)).toBe('品種の名前を入れてください。');
    expect(varietyProblem({ name: '[品種C]', processKind: '' }, [], null)).toBe('加工の種類を選んでください。');
    expect(varietyProblem({ name: '[品種A]', processKind: '[加工A]' }, ['[品種A]'], null)).toBe('[品種A] は、もう登録してあります。');
  });

  it('品種の加工の種類は、使われていないときだけ変えられる', () => {
    expect(varietyProblem({ name: '[品種A]', processKind: '[加工B]' }, ['[品種A]'], variety('[品種A]', { inUse: true }))).toBe(
      '[品種A] は使われているので、加工の種類を変えられません。',
    );
    expect(varietyProblem({ name: '[品種A]', processKind: '[加工B]' }, ['[品種A]'], variety('[品種A]'))).toBeNull();
  });

  it('ID は、どの品種にもまだ対応させていないものだけ加えられる', () => {
    const all = [variety('[品種A]', { ids: ['T-0101'] }), variety('[品種B]', { ids: ['T-0201'] })];
    expect(idProblem(' ', all)).toBe('ID を入れてください。');
    expect(idProblem('T-0201', all)).toBe('T-0201 は、もう [品種B] に対応させてあります。');
    expect(idProblem('T-0102', all)).toBeNull();
  });

  it('在庫にある ID は外せない', () => {
    expect(removeIdBlockedReason(variety('[品種A]', { ids: ['T-0101'], stockedIds: ['T-0101'] }), 'T-0101')).toBe('T-0101 は在庫にあるので、外せません。');
    expect(removeIdBlockedReason(variety('[品種A]', { ids: ['T-0102'] }), 'T-0102')).toBeNull();
  });
});

describe('deviceProblem（装置を保存できない理由）', () => {
  it('ID と受け渡しパネルを入れ、スロットの数は 1 以上の整数', () => {
    expect(deviceProblem(deviceDraft({ id: '' }), [], null)).toBe('装置の ID を入れてください。');
    expect(deviceProblem(deviceDraft({ panel: '' }), [], null)).toBe('受け渡しパネルを選んでください。');
    expect(deviceProblem(deviceDraft({ slots: [] }), [], null)).toBe('ストッカーを 1 つ以上加えてください。');
    expect(deviceProblem(deviceDraft({ slots: ['3', '0'] }), [], null)).toBe('ストッカー2 のスロットの数を、1 以上の整数で入れてください。');
    expect(deviceProblem(deviceDraft({ id: 'DEV-1' }), ['DEV-1'], null)).toBe('DEV-1 は、もう登録してあります。');
    expect(deviceProblem(deviceDraft(), ['DEV-1'], null)).toBeNull();
  });

  it('減らすのは、減るぶんが空のときだけ', () => {
    const draft = deviceDraftOf(dev1);
    expect(deviceProblem({ ...draft, slots: ['3'] }, ['DEV-1'], dev1)).toBe('ストッカー2 に消耗品があるので、減らせません。');
    expect(deviceProblem({ ...draft, slots: ['3', '2'] }, ['DEV-1'], dev1)).toBe('ストッカー2 のスロット3 に消耗品があるので、減らせません。');
    expect(deviceProblem({ ...draft, slots: ['1', '3', '4'] }, ['DEV-1'], dev1)).toBeNull();
  });

  it('受け渡しパネルは、使われていないときだけ変えられる', () => {
    expect(deviceProblem({ ...deviceDraftOf(dev1), panel: 'D7' }, ['DEV-1'], dev1)).toBe('DEV-1 は使われているので、受け渡しパネルを変えられません。');
    expect(deviceProblem({ ...deviceDraftOf(empty), panel: 'D7' }, ['DEV-9'], empty)).toBeNull();
  });
});

describe('panelChoices（選べる受け渡しパネル）', () => {
  it('まだどこにも使われていないもの。直すときは、いまのパネルも', () => {
    expect(panelChoices(['D1', 'D2', 'ST', 'SP'], ['D1', 'ST'], null)).toEqual(['D2', 'SP']);
    expect(panelChoices(['D1', 'D2', 'ST', 'SP'], ['D1', 'ST'], 'D1')).toEqual(['D1', 'D2', 'SP']);
  });
});

describe('stationAddBlockedReason（ステーションを加えるのを押せない理由）', () => {
  it('ステーションは1台だけ', () => {
    expect(stationAddBlockedReason([])).toBeNull();
    expect(stationAddBlockedReason([{ id: 'ST-1', panel: 'ST', busy: false }])).toBe('ステーションは、1台だけ登録できます（ステーションの選び方のルールが決まるまで）。');
  });
});

describe('stationProblem（ステーションを保存できない理由）', () => {
  it('ID と受け渡しパネルを入れ、使われているときはパネルを変えられない', () => {
    const st1 = { id: 'ST-1', panel: 'ST', busy: true };
    expect(stationProblem({ id: '', panel: 'ST' }, [], null)).toBe('ステーションの ID を入れてください。');
    expect(stationProblem({ id: 'ST-2', panel: '' }, [], null)).toBe('受け渡しパネルを選んでください。');
    expect(stationProblem({ id: 'ST-1', panel: 'D2' }, ['ST-1'], st1)).toBe('ST-1 は使われているので、受け渡しパネルを変えられません。');
    expect(stationProblem({ id: 'ST-1', panel: 'D2' }, ['ST-1'], { ...st1, busy: false })).toBeNull();
  });
});
