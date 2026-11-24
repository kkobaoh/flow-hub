import { describe, expect, it } from 'vitest';
import { initialRoles } from '../auth/permissions';
import { fixProblem, shipBlockedReason, shipNote, sortStationItems, stateLabel, type StationItem } from './station';

function item(id: string, state: StationItem['state'] = { kind: 'available' }): StationItem {
  return { id, variety: '[品種A]', condition: '新品', receivedAt: new Date('2026-11-20T14:02:00'), state };
}

describe('sortStationItems（並べ方）', () => {
  it('要確認を上にし、それぞれの中と残りは ID の順', () => {
    const items = [item('T-0110'), item('X-9001', { kind: 'needsCheck', reason: '' }), item('T-0020'), item('A-0001', { kind: 'needsCheck', reason: '' })];

    expect(sortStationItems(items).map((i) => i.id)).toEqual(['A-0001', 'X-9001', 'T-0020', 'T-0110']);
  });
});

describe('shipBlockedReason（出庫を押せない理由）', () => {
  it('届ける搬送に AGV が割り当て済みなら、出庫できない', () => {
    expect(shipBlockedReason(item('T-1', { kind: 'reserved', transport: 14, assigned: true }), initialRoles.運用)).toBe(
      '届ける搬送 #14 に AGV を割り当て済みなので、出庫できません。',
    );
    expect(shipBlockedReason(item('T-1', { kind: 'reserved', transport: 14, assigned: false }), initialRoles.運用)).toBeNull();
  });

  it('出庫を頼んだものは、もう一度は頼めない', () => {
    expect(shipBlockedReason(item('T-1', { kind: 'shipping' }), initialRoles.運用)).toBe('出庫を頼んでいます。');
  });

  it('要確認のものも、直さずに出庫できる', () => {
    expect(shipBlockedReason(item('X-1', { kind: 'needsCheck', reason: '' }), initialRoles.運用)).toBeNull();
  });

  it('パーミッションがなければ、出庫できない', () => {
    expect(shipBlockedReason(item('T-1'), initialRoles.閲覧のみ)).not.toBeNull();
  });
});

describe('shipNote（確かめる画面の文）', () => {
  it('予約や指名が付いていれば、それを解いて出すことを書く', () => {
    expect(shipNote(item('T-1', { kind: 'reserved', transport: 14, assigned: false }))).toContain('届ける予約（搬送 #14）を解いて');
    expect(shipNote(item('T-1', { kind: 'nominated', device: 'DEV-1' }))).toContain('指名（DEV-1）を外して');
    expect(shipNote(item('T-1'))).toContain('記録から消します');
  });
});

describe('stateLabel', () => {
  it('状態を文にする', () => {
    expect(stateLabel({ kind: 'available' })).toBe('届けられる');
    expect(stateLabel({ kind: 'reserved', transport: 14, assigned: false })).toBe('届ける予約あり（搬送 #14）');
    expect(stateLabel({ kind: 'nominated', device: 'DEV-1' })).toBe('指名あり（DEV-1）');
    expect(stateLabel({ kind: 'needsCheck', reason: '' })).toBe('要確認');
    expect(stateLabel({ kind: 'shipping' })).toBe('出庫を頼んだ');
  });
});

describe('fixProblem（直すのを保存できない理由）', () => {
  it('品種と、新品か中古品かを、どちらも選ぶまで保存できない', () => {
    expect(fixProblem({ variety: '', condition: null })).not.toBeNull();
    expect(fixProblem({ variety: '[品種A]', condition: null })).toBe('新品か中古品かを選んでください。');
    expect(fixProblem({ variety: '', condition: '新品' })).toBe('品種を選んでください。');
    expect(fixProblem({ variety: '[品種A]', condition: '中古品' })).toBeNull();
  });
});
