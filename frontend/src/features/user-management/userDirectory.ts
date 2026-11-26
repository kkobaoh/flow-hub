import type { Permission } from '../auth/permissions';
import type { Duration, Role, UserAccount } from './users';

export interface UserDirectory {
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
    canAddUser: roles.some(role => role.id === currentRole && role.fixed),
    ...(Number.isFinite(firstExpiry) && { refreshAfterMs: Math.max(1, firstExpiry - now) }),
    users: users.map(user => ({
      id: user.id, role: user.role, lastLogin: user.lastLogin ? new Date(user.lastLogin) : null,
      state: user.lockedUntil ? { kind: 'locked', until: new Date(user.lockedUntil) }
        : { kind: user.mustChangePassword ? 'mustChangePassword' : 'active' },
    })),
    roles: roles.map(role => ({
      id: role.id, name: role.name, fixed: role.fixed, isDefault: role.isDefault,
      timeout: duration(role.idleTimeoutMinutes), permissions: new Set(role.permissions),
    })),
  };
}

export async function addUser(userId: string): Promise<string> {
  const form = await fetch('/identity/user/session', { cache: 'no-store' });
  if (!form.ok) throw new Error('ログイン状態を確認してください。');
  const { requestToken } = await form.json();
  const response = await fetch('/identity/user/users', {
    method: 'POST', cache: 'no-store',
    headers: { 'Content-Type': 'application/json', 'X-FlowHub-CSRF': requestToken },
    body: JSON.stringify({ userId }),
  });
  if (response.status === 409) throw new Error('このユーザー ID はすでに登録されています。');
  if (response.status === 403) throw new Error('ユーザーを加えるには管理者ロールが必要です。');
  if (!response.ok) throw new Error('ユーザーの追加結果を確認できませんでした。一覧を確認してください。');
  return (await response.json()).temporaryPassword;
}
