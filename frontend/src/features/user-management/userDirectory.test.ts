import { afterEach, expect, it, vi } from 'vitest';
import { addUser, DirectoryAccessError, fetchUserDirectory } from './userDirectory';

afterEach(() => vi.unstubAllGlobals());

it('ログイン中の利用者のトークンを付けて追加し、一度だけ発行されたパスワードを返す', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(Response.json({ requestToken: 'csrf' }))
    .mockResolvedValueOnce(Response.json({ temporaryPassword: 'issued' }));
  vi.stubGlobal('fetch', fetch);
  expect(await addUser('ops')).toBe('issued');
  expect(fetch).toHaveBeenLastCalledWith('/identity/user/users', expect.objectContaining({
    method: 'POST', headers: expect.objectContaining({ 'X-FlowHub-CSRF': 'csrf' }), body: JSON.stringify({ userId: 'ops' }),
  }));
});

it('変更待ちでもロック中を優先し、ロックのない変更待ちと記録のない最終ログインも表示できる', async () => {
  const until = '2026-11-26T08:00:00Z';
  const user = { id: 'first', role: 'admin', mustChangePassword: true, lockedUntil: until, lastLogin: null };
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(Response.json([user, { ...user, lockedUntil: null, lastLogin: until }]))
    .mockResolvedValueOnce(Response.json([])).mockResolvedValueOnce(Response.json({ userId: 'first', permissions: [] })));
  const { users, canAddUser } = await fetchUserDirectory(new AbortController().signal);
  expect(canAddUser).toBe(false);
  expect(users[0]?.state).toEqual({ kind: 'locked', until: new Date(until) });
  expect(users[0]?.lastLogin).toBeNull();
  expect(users[1]?.state).toEqual({ kind: 'mustChangePassword' });
  expect(users[1]?.lastLogin).toEqual(new Date(until));
});

it('通常ログインがないときはロールの読み込みに進まない', async () => {
  const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 401 }));
  vi.stubGlobal('fetch', fetch);
  await expect(fetchUserDirectory(new AbortController().signal)).rejects.toBeInstanceOf(DirectoryAccessError);
  expect(fetch).toHaveBeenCalledTimes(1);
});

it('ロールの無操作時間と権限を既存の表示形式に変換する', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(Response.json([])).mockResolvedValueOnce(Response.json([
    { id: 'viewer', name: '閲覧のみ', isDefault: true, fixed: false, idleTimeoutMinutes: 10080, permissions: [] },
    { id: 'admin', name: '管理者', isDefault: false, fixed: true, idleTimeoutMinutes: 480, permissions: ['userAdmin'] },
  ])).mockResolvedValueOnce(Response.json({ userId: 'first', permissions: ['userAdmin'] }));
  vi.stubGlobal('fetch', fetch);
  const { roles, canAddUser } = await fetchUserDirectory(new AbortController().signal);
  expect(canAddUser).toBe(true);
  expect(roles[0]?.timeout).toEqual({ amount: 7, unit: 'days' });
  expect(roles[1]?.timeout).toEqual({ amount: 8, unit: 'hours' });
  expect(roles[1]?.permissions.has('userAdmin')).toBe(true);
});
