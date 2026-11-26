import { act } from 'react';
import { expect, it, vi } from 'vitest';
import { change, click, render } from '../testing/render';
import { FirstAdministratorPage } from './FirstAdministratorPage';
import { SetupPage } from './SetupPage';

it('setup のパスワードでログインしてから管理者 ID を入力する', async () => {
  const login = vi.fn(async () => {});
  const { container } = render(<SetupPage login={login} />);
  change(container.querySelector('input[type=password]')!, 'initial');
  click(container.querySelector('button')!);
  await act(async () => {});
  expect(login).toHaveBeenCalledWith('initial');
  expect(container.textContent).toContain('管理者 ID');
  expect(container.querySelector('input[type=password]')).toBeNull();
});

it('予約 ID を断り、作成に成功したときだけ仮パスワードを一度表示する', async () => {
  const create = vi.fn(async () => 'temporary-value');
  const { container } = render(<FirstAdministratorPage create={create} />);
  const field = container.querySelector('input')!;
  const submit = container.querySelector('button')!;
  change(field, 'setup');
  expect(submit.disabled).toBe(true);
  change(field, ' admin01 ');
  click(submit);
  await act(async () => {});
  expect(create).toHaveBeenCalledWith('admin01');
  expect(container.textContent).toContain('temporary-value');
  click(container.querySelector('button')!);
  expect(container.textContent).not.toContain('temporary-value');
  expect(container.querySelector('input')).toBeNull();
});
