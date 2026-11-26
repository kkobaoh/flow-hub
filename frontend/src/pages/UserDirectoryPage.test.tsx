import { act } from 'react';
import { expect, it, vi } from 'vitest';
import { click, render } from '../testing/render';
import { UserDirectoryPage } from './UserDirectoryPage';

it('保存済みのユーザーとロールを表示し、未接続の変更操作は実行させない', async () => {
  const load = vi.fn(async () => ({
    canAddUser: false,
    users: [{ id: 'first-admin', role: 'admin', state: { kind: 'active' as const }, lastLogin: null }],
    roles: [{ id: 'admin', name: '現場管理者', isDefault: false, fixed: true, timeout: { amount: 8, unit: 'hours' as const }, permissions: new Set<'userAdmin'>(['userAdmin']) }],
  }));
  const { container } = render(<UserDirectoryPage load={load} />);
  await act(async () => {});
  expect(container.textContent).toContain('first-admin');
  expect(container.querySelector('select')?.disabled).toBe(true);
  click(container.querySelectorAll('[role=tab]')[1]!);
  expect(container.textContent).toContain('現場管理者');
});

it('開いたままロック期限に達したらサーバーから状態を読み直す', async () => {
  vi.useFakeTimers();
  try {
    const load = vi.fn().mockResolvedValueOnce({ users: [], roles: [], refreshAfterMs: 5000 })
      .mockResolvedValue({ users: [], roles: [] });
    render(<UserDirectoryPage load={load} />);
    await act(async () => {});
    await act(async () => { await vi.advanceTimersByTimeAsync(5000); });
    expect(load).toHaveBeenCalledTimes(2);
  } finally {
    vi.useRealTimers();
  }
});
