import type { Permission } from '../auth/permissions';
import type { Duration, Role, UserAccount } from './users';

export interface UserDirectory {
  readonly currentUserId: string;
  readonly users: readonly UserAccount[];
  readonly roles: readonly Role[];
  readonly refreshAfterMs?: number;
  readonly canAddUser: boolean;
}

interface RoleWire {
  id: string;
  name: string;
  fixed: boolean;
  isDefault: boolean;
  idleTimeoutMinutes: number;
  permissions: Permission[];
}

interface UserWire {
  id: string;
  role: string;
  lastLogin: string | null;
  mustChangePassword: boolean;
  lockedUntil: string | null;
  lockToken: string | null;
}

export class DirectoryAccessError extends Error {}

async function read(name: string, signal: AbortSignal): Promise<Response> {
  const response = await fetch('/identity/user/' + name, { signal, cache: 'no-store' });
  if (response.status === 401 || response.status === 403) throw new DirectoryAccessError();
  if (!response.ok) throw new Error('一覧を取得できませんでした。');
  return response;
}

function duration(minutes: number): Duration {
  if (minutes % 1440 === 0) return { amount: minutes / 1440, unit: 'days' };
  if (minutes % 60 === 0) return { amount: minutes / 60, unit: 'hours' };
  return { amount: minutes, unit: 'minutes' };
}

export async function fetchUserDirectory(signal: AbortSignal): Promise<UserDirectory> {
  const response = await read('users', signal);
  const serverTime = Date.parse(response.headers.get('Date') ?? '');
  const readAt = performance.now();
  const users: UserWire[] = await response.json();
  const roles: RoleWire[] = await (await read('roles', signal)).json();
  const account: { userId: string } = await (await read('account', signal)).json();
  const currentRole = users.find(user => user.id === account.userId)?.role;
  const firstExpiry = users.reduce((first, user) => user.lockedUntil ? Math.min(first, Date.parse(user.lockedUntil)) : first, Infinity);
  const now = Number.isFinite(serverTime) ? serverTime + (performance.now() - readAt) : Date.now();
  return {
    currentUserId: account.userId,
    canAddUser: roles.some(role => role.id === currentRole && role.fixed),
    ...(Number.isFinite(firstExpiry) && { refreshAfterMs: Math.max(1, firstExpiry - now) }),
    users: users.map(user => ({
      id: user.id, role: user.role, lastLogin: user.lastLogin ? new Date(user.lastLogin) : null,
      state: user.lockedUntil ? { kind: 'locked', until: new Date(user.lockedUntil), ...(user.lockToken && { lockToken: user.lockToken }) }
        : { kind: user.mustChangePassword ? 'mustChangePassword' : 'active' },
    })),
    roles: roles.map(role => ({
      id: role.id, name: role.name, fixed: role.fixed, isDefault: role.isDefault,
      timeout: duration(role.idleTimeoutMinutes), permissions: new Set(role.permissions),
    })),
  };
}

async function postDirectory(path: string, data: Record<string, string>): Promise<Response> {
  const form = await fetch('/identity/user/session', { cache: 'no-store' });
  if (!form.ok) throw new Error('ログイン状態を確認してください。');
  const { requestToken } = await form.json();
  return fetch('/identity/user/' + path, {
    method: 'POST', cache: 'no-store',
    headers: { 'Content-Type': 'application/json', 'X-FlowHub-CSRF': requestToken },
    body: JSON.stringify(data),
  });
}

export async function setDefaultRole(roleId: string): Promise<void> {
  const response = await postDirectory('roles/default', { roleId });
  if (response.status === 403) throw new Error('デフォルトロールの変更には管理者権限が必要です。');
  if (response.status === 404) throw new Error('指定したロールが見つかりません。一覧を確認してください。');
  if (!response.ok) throw new Error('デフォルトロールの変更結果を確認できませんでした。');
}

export async function changeUserRole(userId: string, roleId: string): Promise<void> {
  const response = await postDirectory('users/role', { userId, roleId });
  if (response.status === 409) throw new Error('最後の管理者のロールは変更できません。');
  if (response.status === 403) throw new Error('ロールの変更には管理者権限が必要です。');
  if (!response.ok) throw new Error('ロールの変更結果を確認できませんでした。一覧を確認してください。');
}

export async function unlockUser(userId: string, lockToken: string): Promise<void> {
  const response = await postDirectory('users/unlock', { userId, lockToken });
  if (response.status === 409) throw new Error('表示後に新しいロックがかかりました。一覧を確認してください。');
  if (response.status === 403) throw new Error('ロックの解除には管理者権限が必要です。');
  if (!response.ok) throw new Error('ロックの解除結果を確認できませんでした。');
}

export async function addUser(userId: string): Promise<string> {
  const response = await postDirectory('users', { userId });
  if (response.status === 409) throw new Error('このユーザー ID はすでに登録されています。');
  if (response.status === 403) throw new Error('ユーザーを加えるには管理者ロールが必要です。');
  if (!response.ok) throw new Error('ユーザーの追加結果を確認できませんでした。一覧を確認してください。');
  return (await response.json()).temporaryPassword;
}

export async function resetUserPassword(userId: string): Promise<string> {
  const response = await postDirectory('users/temporary-password', { userId });
  if (response.status === 403) throw new Error('このユーザーの仮パスワードを再発行する権限がありません。');
  if (!response.ok) throw new Error('仮パスワードの再発行結果を確認できませんでした。');
  return (await response.json()).temporaryPassword;
}
