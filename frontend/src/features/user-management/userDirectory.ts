import type { Permission } from '../auth/permissions';
import type { Duration, Role, UserAccount } from './users';

export interface UserDirectory {
  readonly users: readonly UserAccount[];
  readonly roles: readonly Role[];
  readonly refreshAfterMs?: number;
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
  const firstExpiry = users.reduce((first, user) => user.lockedUntil ? Math.min(first, Date.parse(user.lockedUntil)) : first, Infinity);
  const now = Number.isFinite(serverTime) ? serverTime + (performance.now() - readAt) : Date.now();
  return {
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
