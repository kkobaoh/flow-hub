import { act } from 'react';
import { expect, it, vi } from 'vitest';
import { render, change, click } from '../testing/render';
import { UserLoginPage } from './UserLoginPage';

it('初回変更が済むまで業務画面へのリンクを出さない', async () => {
  const session = vi.fn().mockResolvedValueOnce({ userId: 'Admin', mustChangePassword: true, minimumLength: 8, requestToken: 'a' })
    .mockResolvedValue({ userId: 'Admin', mustChangePassword: false, minimumLength: 8, requestToken: 'b' });
  const update = vi.fn(async () => {});
  const api = { session, update, login: vi.fn(), logout: vi.fn() };
  const { container } = render(<UserLoginPage api={api} />);
  await act(async () => {});
  expect(container.querySelector('a')).toBeNull();
  change(container.querySelector('input')!, ' 新しいパスワード');
  click(container.querySelector('button')!);
  await act(async () => {});
  expect(update).toHaveBeenCalledWith(' 新しいパスワード', 'a');
  expect(container.querySelector('a')?.getAttribute('href')).toBe('/');
});
