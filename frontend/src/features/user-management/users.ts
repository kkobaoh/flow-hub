import type { Permission, Permissions } from '../auth/permissions';
import { clockOf } from '../../shared/utils/dateTime';

export type DurationUnit = 'minutes' | 'hours' | 'days';

export const durationUnitLabel: Record<DurationUnit, string> = { minutes: '分', hours: '時間', days: '日' };

/** 操作がないままログインしておける時間。 */
export interface Duration {
  readonly amount: number;
  readonly unit: DurationUnit;
}

/** ロール。id は変わらない（名前は変えられる）。fixed は管理者のロール（名前とパーミッションを変えられず、消せない）。 */
export interface Role {
  readonly id: string;
  readonly name: string;
  readonly permissions: Permissions;
  readonly timeout: Duration;
  readonly isDefault: boolean;
  readonly fixed: boolean;
}

export type UserState = { readonly kind: 'active' } | { readonly kind: 'locked'; readonly until: Date } | { readonly kind: 'mustChangePassword' };

export interface UserAccount {
  readonly id: string;
  /** ロールの id。 */
  readonly role: string;
  readonly lastLogin: Date | null;
  readonly state: UserState;
}

export interface RoleDraft {
  readonly name: string;
  readonly amount: string;
  readonly unit: DurationUnit;
}

const NO_PERMISSION = 'ユーザーとロールの管理は、管理者のロールで使えます。';

export function userAdminBlockedReason(permissions: Permissions): string | null {
  return permissions.has('userAdmin') ? null : NO_PERMISSION;
}

/** ユーザーのロールを付け替えられない理由。最後の管理者は付け替えられない。 */
export function roleChangeBlockedReason(user: UserAccount, users: readonly UserAccount[], roles: readonly Role[], permissions: Permissions): string | null {
  const blocked = userAdminBlockedReason(permissions);
  if (blocked !== null) {
    return blocked;
  }
  const admin = roles.find((r) => r.fixed);
  if (admin !== undefined && user.role === admin.id && users.filter((u) => u.role === admin.id).length <= 1) {
    return '最後の管理者なので、ロールを付け替えられません。';
  }
  return null;
}

/** ロールを消せない理由。管理者のロール、デフォルトのロール、ユーザーに付けてあるロールは消せない。 */
export function roleDeleteBlockedReason(role: Role, users: readonly UserAccount[]): string | null {
  if (role.fixed) {
    return '管理者のロールは消せません。';
  }
  if (role.isDefault) {
    return 'デフォルトのロールは消せません。';
  }
  const count = users.filter((u) => u.role === role.id).length;
  return count === 0 ? null : `${count}人のユーザーに付けてあるので、消せません。先にロールを付け替えてください。`;
}

export function userProblem(id: string, existing: readonly string[]): string | null {
  const i = id.trim();
  if (i === '') {
    return 'ユーザー ID を入れてください。';
  }
  return existing.includes(i) ? `${i} は、もう使っています。` : null;
}

/** ロールを保存できない理由。original は直すロールの id（加えるときは null）。 */
export function roleProblem(draft: RoleDraft, roles: readonly Role[], original: string | null): string | null {
  const name = draft.name.trim();
  if (name === '') {
    return 'ロールの名前を入れてください。';
  }
  if (roles.some((r) => r.name === name && r.id !== original)) {
    return `${name} は、もう使っています。`;
  }
  if (!/^\d+$/.test(draft.amount.trim()) || Number(draft.amount) < 1) {
    return 'ログインしておける時間を、1 以上の整数で入れてください。';
  }
  return null;
}

export function durationLabel(d: Duration): string {
  return `${d.amount}${durationUnitLabel[d.unit]}`;
}

export function stateLabel(state: UserState): string {
  switch (state.kind) {
    case 'active':
      return '使える';
    case 'locked':
      return `ロック中（ログインの失敗が続いたため。${clockOf(state.until)} まで）`;
    case 'mustChangePassword':
      return '仮のパスワード（次のログインで変更を求める）';
  }
}

/** どれかのロールのパーミッションが変わったか。 */
export function permissionsChanged(before: ReadonlyMap<string, ReadonlySet<Permission>>, after: ReadonlyMap<string, ReadonlySet<Permission>>): boolean {
  for (const [id, set] of after) {
    const old = before.get(id);
    if (old === undefined || old.size !== set.size || [...set].some((p) => !old.has(p))) {
      return true;
    }
  }
  return false;
}
