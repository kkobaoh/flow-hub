import { describe, expect, it } from 'vitest';
import { agvId } from './agv';
import {
  agvChangeBlockedReason,
  agvProblem,
  sortAgvEntries,
  toAgvType,
  typeDeleteBlockedReason,
  typeDraftOf,
  typeProblem,
  type AgvEntry,
  type AgvType,
  type TypeDraft,
} from './agvs';
import { initialRoles } from '../auth/permissions';

const 管理者 = initialRoles.管理者;

function entry(id: string, overrides: Partial<AgvEntry> = {}): AgvEntry {
  return { id: agvId(id), type: '[種類A]', carrier: '[キャリアA]', control: 'detached', registration: 'both', ...overrides };
}

const typeA: AgvType = { id: '[種類A]', name: '[種類A]', forward: 1000, backward: 500, rotation: 45, chargeAt: 20, resumeAt: 80, special: false };
const defaults = { chargeAt: 20, resumeAt: 80 };
const draft = (overrides: Partial<TypeDraft> = {}): TypeDraft => ({
  id: 'type-c',
  name: '[種類C]',
  forward: '1000',
  backward: '500',
  rotation: '45',
  chargeAt: '',
  resumeAt: '',
  special: false,
  ...overrides,
});

describe('sortAgvEntries（並べ方）', () => {
  it('登録が食い違っているものを上にし、それぞれの中と残りは番号の順', () => {
    const list = [entry('AGV-10'), entry('AGV-4', { registration: 'trafficOnly' }), entry('AGV-2')];

    expect(sortAgvEntries(list).map((a) => a.id)).toEqual(['AGV-4', 'AGV-2', 'AGV-10']);
  });
});

describe('agvChangeBlockedReason（AGV を変える・消すのを押せない理由）', () => {
  it('切り離しのときだけ変えたり消したりできる', () => {
    expect(agvChangeBlockedReason(entry('AGV-1'), 管理者)).toBeNull();
    expect(agvChangeBlockedReason(entry('AGV-1', { control: 'auto' }), 管理者)).toBe('切り離しのときだけ、変えたり消したりできます（いまはオート）。');
  });

  it('管理者でなければ使えない', () => {
    expect(agvChangeBlockedReason(entry('AGV-1'), initialRoles.保守)).toBe('AGV と AGV の種類の登録は、管理者のロールで使えます。');
  });
});

describe('agvProblem（AGV を保存できない理由）', () => {
  it('加えるときは、番号・種類・キャリアの種類をすべて入れ、使っている番号は使えない', () => {
    const ok = { id: 'AGV-5', type: '[種類A]', carrier: '[キャリアA]' };
    expect(agvProblem({ ...ok, id: '' }, ['AGV-1'])).toBe('AGV の番号を入れてください。');
    expect(agvProblem({ ...ok, type: '' }, ['AGV-1'])).toBe('AGV の種類を選んでください。');
    expect(agvProblem({ ...ok, carrier: '' }, ['AGV-1'])).toBe('キャリアの種類を選んでください。');
    expect(agvProblem({ ...ok, id: 'AGV-1' }, ['AGV-1'])).toBe('AGV-1 は、もう登録してあります。');
    expect(agvProblem(ok, ['AGV-1'])).toBeNull();
  });
});

describe('sortAgvEntries（種類が未定の AGV）', () => {
  it('種類が未定の AGV も、食い違いと同じく上に並べる', () => {
    expect(sortAgvEntries([entry('AGV-1'), entry('AGV-9', { type: null, carrier: null, control: null, registration: 'unconfigured' })]).map((e) => e.id)).toEqual(['AGV-9', 'AGV-1']);
  });
});

describe('typeProblem（AGV の種類を保存できない理由）', () => {
  it('速さは 1 以上の整数で、必ず入れる', () => {
    expect(typeProblem(draft({ forward: '' }), { mode: 'add', existingIds: [], existingNames: [], defaults })).toBe('前進の速さを、1 以上の整数で入れてください。');
    expect(typeProblem(draft({ backward: '0' }), { mode: 'add', existingIds: [], existingNames: [], defaults })).toBe('後退の速さを、1 以上の整数で入れてください。');
    expect(typeProblem(draft({ rotation: '4.5' }), { mode: 'add', existingIds: [], existingNames: [], defaults })).toBe('回転の速さを、1 以上の整数で入れてください。');
  });

  it('充電の値は 0〜100 の整数で、加えるときだけ空にでき、空なら既定値で埋める', () => {
    expect(typeProblem(draft(), { mode: 'add', existingIds: [], existingNames: [], defaults })).toBeNull();
    expect(toAgvType(draft(), defaults)).toMatchObject({ chargeAt: 20, resumeAt: 80 });
    expect(typeProblem(draft({ chargeAt: '101' }), { mode: 'add', existingIds: [], existingNames: [], defaults })).toBe('充電に向かう残量を、0〜100 の整数で入れてください。');
    expect(typeProblem(draft({ chargeAt: '20', resumeAt: '' }), { mode: 'edit', existingNames: [], defaults })).toBe('割り当てに戻す残量を、0〜100 の整数で入れてください。');
  });

  it('割り当てに戻す残量は、充電に向かう残量以上（同じ値はよい）。既定値で埋めたあとで確かめる', () => {
    expect(typeProblem(draft({ chargeAt: '50', resumeAt: '40' }), { mode: 'add', existingIds: [], existingNames: [], defaults })).toBe('割り当てに戻す残量は、充電に向かう残量（50%）以上にしてください。');
    expect(typeProblem(draft({ chargeAt: '50', resumeAt: '50' }), { mode: 'add', existingIds: [], existingNames: [], defaults })).toBeNull();
    expect(typeProblem(draft({ chargeAt: '90' }), { mode: 'add', existingIds: [], existingNames: [], defaults })).toBe('割り当てに戻す残量は、充電に向かう残量（90%）以上にしてください。');
  });

  it('名前は必ず入れ、ほかの種類と同じ名前は使えない', () => {
    expect(typeProblem(draft({ name: ' ' }), { mode: 'add', existingIds: [], existingNames: [], defaults })).toBe('種類の名前を入れてください。');
    expect(typeProblem(draft({ name: '[種類A]' }), { mode: 'add', existingIds: [], existingNames: ['[種類A]'], defaults })).toBe('[種類A] という名前の種類は、もう登録してあります。');
    expect(typeProblem(draft({ name: '[種類A]', chargeAt: '20', resumeAt: '80' }), { mode: 'edit', existingNames: [], defaults })).toBeNull(); // 変えるときは、自分の名前は数えない
  });

  it('加えるときは、ID を英数字と「-」「_」で入れ、使っている ID は使えない', () => {
    expect(typeProblem(draft({ id: '' }), { mode: 'add', existingIds: [], existingNames: [], defaults })).toBe('種類の ID を入れてください。');
    expect(typeProblem(draft({ id: '小型' }), { mode: 'add', existingIds: [], existingNames: [], defaults })).toBe('種類の ID は、英数字と「-」「_」で入れてください。');
    expect(typeProblem(draft({ id: 'type-c' }), { mode: 'add', existingIds: ['type-c'], existingNames: [], defaults })).toBe('ID type-c の種類は、もう登録してあります。');
    expect(typeProblem(draft({ id: '' }), { mode: 'edit', existingNames: [], defaults })).toBe('充電に向かう残量を、0〜100 の整数で入れてください。'); // 変えるときは ID を確かめない
  });

  it('直すときの下書きは、いまの値を入れる', () => {
    expect(typeDraftOf(typeA)).toEqual({ id: '[種類A]', name: '[種類A]', forward: '1000', backward: '500', rotation: '45', chargeAt: '20', resumeAt: '80', special: false });
  });
});

describe('typeDeleteBlockedReason（種類を消すのを押せない理由）', () => {
  it('使う AGV があれば消せない', () => {
    expect(typeDeleteBlockedReason(typeA, [entry('AGV-2'), entry('AGV-1'), entry('AGV-3', { type: '[種類B]' })], 管理者)).toBe(
      'AGV-1、AGV-2 が使っているので、消せません。',
    );
    expect(typeDeleteBlockedReason(typeA, [entry('AGV-3', { type: '[種類B]' })], 管理者)).toBeNull();
  });
});
