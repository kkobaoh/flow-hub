import { describe, expect, it } from 'vitest';
import { initialRoles } from '../auth/permissions';
import {
  durationLabel,
  permissionsChanged,
  roleChangeBlockedReason,
  roleDeleteBlockedReason,
  roleProblem,
  stateLabel,
  userProblem,
  type Role,
  type UserAccount,
} from './users';

const role = (id: string, overrides: Partial<Role> = {}): Role => ({
  id,
  name: id,
  permissions: new Set(),
  timeout: { amount: 8, unit: 'hours' },
  isDefault: false,
  fixed: false,
  ...overrides,
});

const roles: Role[] = [role('viewer', { name: '閲覧のみ', isDefault: true }), role('ops', { name: '運用' }), role('admin', { name: '管理者', fixed: true })];
const user = (id: string, roleId: string): UserAccount => ({ id, role: roleId, lastLogin: null, state: { kind: 'active' } });

describe('roleChangeBlockedReason（ロールを付け替えられない理由）', () => {
  it('最後の管理者は付け替えられない', () => {
    expect(roleChangeBlockedReason(user('admin01', 'admin'), [user('admin01', 'admin'), user('ops01', 'ops')], roles, initialRoles.管理者)).toBe(
      '最後の管理者なので、ロールを付け替えられません。',
    );
  });

  it('管理者がほかにもいれば、自分も含めて付け替えられる', () => {
    const users = [user('admin01', 'admin'), user('admin02', 'admin')];
    expect(roleChangeBlockedReason(users[0]!, users, roles, initialRoles.管理者)).toBeNull();
  });

  it('管理者のロールでなければ使えない', () => {
    expect(roleChangeBlockedReason(user('ops01', 'ops'), [user('ops01', 'ops')], roles, initialRoles.保守)).toBe('ユーザーとロールの管理は、管理者のロールで使えます。');
  });
});

describe('roleDeleteBlockedReason（ロールを消せない理由）', () => {
  it('管理者のロールとデフォルトのロールは消せない', () => {
    expect(roleDeleteBlockedReason(roles[2]!, [])).toBe('管理者のロールは消せません。');
    expect(roleDeleteBlockedReason(roles[0]!, [])).toBe('デフォルトのロールは消せません。');
  });

  it('ユーザーがいるロールは消せない', () => {
    expect(roleDeleteBlockedReason(roles[1]!, [user('ops01', 'ops'), user('ops02', 'ops')])).toBe('2人のユーザーに付けてあるので、消せません。先にロールを付け替えてください。');
    expect(roleDeleteBlockedReason(roles[1]!, [])).toBeNull();
  });
});

describe('userProblem と roleProblem（保存できない理由）', () => {
  it('ユーザー ID は必ず入れ、使っている ID は使えない', () => {
    expect(userProblem(' ', ['admin01'])).toBe('ユーザー ID を入れてください。');
    expect(userProblem('admin01', ['admin01'])).toBe('admin01 は、もう使っています。');
    expect(userProblem('ops03', ['admin01'])).toBeNull();
  });

  it('ロールは、名前と時間（1 以上の整数）を入れ、同じ名前は使えない', () => {
    expect(roleProblem({ name: '', amount: '8', unit: 'hours' }, roles, null)).toBe('ロールの名前を入れてください。');
    expect(roleProblem({ name: '運用', amount: '8', unit: 'hours' }, roles, null)).toBe('運用 は、もう使っています。');
    expect(roleProblem({ name: '運用', amount: '4', unit: 'hours' }, roles, 'ops')).toBeNull();
    expect(roleProblem({ name: '夜勤', amount: '0', unit: 'hours' }, roles, null)).toBe('ログインしておける時間を、1 以上の整数で入れてください。');
    expect(roleProblem({ name: '夜勤', amount: '1.5', unit: 'hours' }, roles, null)).not.toBeNull();
  });
});

describe('表示', () => {
  it('時間を文にする', () => {
    expect(durationLabel({ amount: 8, unit: 'hours' })).toBe('8時間');
    expect(durationLabel({ amount: 7, unit: 'days' })).toBe('7日');
    expect(durationLabel({ amount: 30, unit: 'minutes' })).toBe('30分');
  });

  it('ユーザーの状態を文にする', () => {
    expect(stateLabel({ kind: 'active' })).toBe('使える');
    expect(stateLabel({ kind: 'locked', until: new Date('2026-11-22T10:15:00') })).toBe('ロック中（ログインの失敗が続いたため。10:15 まで）');
    expect(stateLabel({ kind: 'mustChangePassword' })).toBe('仮のパスワード（次のログインで変更を求める）');
  });
});

describe('permissionsChanged', () => {
  it('どれかのロールのパーミッションが変わったか', () => {
    const before = new Map([['ops', new Set(['operation'] as const)]]);
    expect(permissionsChanged(before, new Map([['ops', new Set(['operation'] as const)]]))).toBe(false);
    expect(permissionsChanged(before, new Map([['ops', new Set(['operation', 'manual'] as const)]]))).toBe(true);
  });
});
