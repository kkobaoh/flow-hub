import { act } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { change, click, render } from '../test/render';
import { initialRoles } from './permissions';
import type { Role, UserAccount } from './users';
import { UsersScreen, type UsersScreenProps } from './UsersScreen';

const roles: Role[] = [
  { id: 'viewer', name: '閲覧のみ', permissions: initialRoles.閲覧のみ, timeout: { amount: 7, unit: 'days' }, isDefault: true, fixed: false },
  { id: 'ops', name: '運用', permissions: initialRoles.運用, timeout: { amount: 8, unit: 'hours' }, isDefault: false, fixed: false },
  { id: 'night', name: '夜勤', permissions: initialRoles.閲覧のみ, timeout: { amount: 8, unit: 'hours' }, isDefault: false, fixed: false },
  { id: 'admin', name: '管理者', permissions: initialRoles.管理者, timeout: { amount: 8, unit: 'hours' }, isDefault: false, fixed: true },
];
const users: UserAccount[] = [
  { id: 'admin01', role: 'admin', lastLogin: new Date('2026-11-22T08:02:00'), state: { kind: 'active' } },
  { id: 'ops01', role: 'ops', lastLogin: null, state: { kind: 'locked', until: new Date('2026-11-22T10:15:00') } },
];

function setup(overrides: Partial<UsersScreenProps> = {}) {
  const handlers = {
    onChangeRole: vi.fn(),
    onUnlock: vi.fn(),
    onResetPassword: vi.fn(() => Promise.resolve('Temp-Pass-1234')),
    onAddUser: vi.fn(() => Promise.resolve('New-Pass-5678')),
    onSaveRole: vi.fn(),
    onDeleteRole: vi.fn(),
    onMakeDefault: vi.fn(),
    onSavePermissions: vi.fn(),
  };
  const view = render(<UsersScreen users={users} roles={roles} permissions={initialRoles.管理者} {...handlers} {...overrides} />);
  return { ...view, ...handlers };
}

const byText = (root: Element, text: string) => [...root.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent === text)!;
const tab = (c: HTMLElement, name: string) => [...c.querySelectorAll<HTMLButtonElement>('[role="tab"]')].find((t) => t.textContent === name)!;
const row = (c: HTMLElement, first: string) => [...c.querySelectorAll('tbody tr')].find((r) => r.querySelector('td, th')?.textContent === first)!;
const flush = () => act(() => Promise.resolve());

describe('UsersScreen（ユーザーとロールの管理）', () => {
  it('ユーザーのロールを付け替える。最後の管理者は付け替えられない', () => {
    const { container, onChangeRole } = setup();

    expect(row(container, 'admin01').querySelector('select')!.disabled).toBe(true);
    expect(row(container, 'admin01').textContent).toContain('最後の管理者なので');
    change(row(container, 'ops01').querySelector('select')!, 'night');

    expect(onChangeRole).toHaveBeenCalledWith('ops01', 'night');
  });

  it('ロックされた人のロックを外す', () => {
    const { container, onUnlock } = setup();

    expect(row(container, 'ops01').textContent).toContain('ロック中');
    click(byText(row(container, 'ops01'), 'ロックを外す'));

    expect(onUnlock).toHaveBeenCalledWith('ops01');
  });

  it('仮のパスワードにする：確かめてから作り、一度だけ見せる', async () => {
    const { container, onResetPassword } = setup();

    click(byText(row(container, 'ops01'), '仮のパスワードにする'));
    click(byText(container.querySelector('[role="alertdialog"]')!, '仮のパスワードにする'));
    await flush();

    expect(onResetPassword).toHaveBeenCalledWith('ops01');
    expect(container.querySelector('[role="dialog"]')?.textContent).toContain('Temp-Pass-1234');
    click(byText(container.querySelector('[role="dialog"]')!, '閉じる'));
    expect(container.textContent).not.toContain('Temp-Pass-1234');
  });

  it('ユーザーを加えると、デフォルトのロールを付け、仮のパスワードを見せる', async () => {
    const { container, onAddUser } = setup();

    click(byText(container, 'ユーザーを加える'));
    const d = container.querySelector('[role="dialog"]')!;
    expect(d.textContent).toContain('閲覧のみ');
    change(d.querySelector<HTMLInputElement>('input[aria-label="ユーザー ID"]')!, 'ops02');
    click(byText(d, 'ユーザーを加える'));
    await flush();

    expect(onAddUser).toHaveBeenCalledWith('ops02');
    expect(container.querySelector('[role="dialog"]')?.textContent).toContain('New-Pass-5678');
  });

  it('ロール：デフォルトにする、消す（デフォルトと管理者とユーザーがいるものは消せない）', () => {
    const { container, onMakeDefault, onDeleteRole } = setup();

    click(tab(container, 'ロール'));
    expect(byText(row(container, '閲覧のみ'), '消す').disabled).toBe(true);
    expect(byText(row(container, '運用'), '消す').disabled).toBe(true);
    expect(row(container, '管理者').querySelector('button')?.textContent).not.toBe('デフォルトにする');

    click(byText(row(container, '夜勤'), 'デフォルトにする'));
    expect(onMakeDefault).toHaveBeenCalledWith('night');

    click(byText(row(container, '夜勤'), '消す'));
    click(byText(container.querySelector('[role="alertdialog"]')!, 'ロールを消す'));
    expect(onDeleteRole).toHaveBeenCalledWith('night');
  });

  it('ロールを加える：時間は数と単位で入れる', () => {
    const { container, onSaveRole } = setup();

    click(tab(container, 'ロール'));
    click(byText(container, 'ロールを加える'));
    const d = container.querySelector('[role="dialog"]')!;
    expect(d.querySelector<HTMLInputElement>('input[aria-label="ログインしておける時間"]')!.value).toBe('8');
    change(d.querySelector<HTMLInputElement>('input[aria-label="ロールの名前"]')!, '点検');
    change(d.querySelector<HTMLInputElement>('input[aria-label="ログインしておける時間"]')!, '30');
    change(d.querySelector<HTMLSelectElement>('select[aria-label="単位"]')!, 'minutes');
    click(byText(d, 'ロールを保存する'));

    expect(onSaveRole).toHaveBeenCalledWith(null, { name: '点検', timeout: { amount: 30, unit: 'minutes' } });
  });

  it('パーミッション：管理者の列と「すべての画面を見る」は変えられず、変えたら保存できる', () => {
    const { container, onSavePermissions } = setup();

    click(tab(container, 'ロール'));
    const box = (label: string) => container.querySelector<HTMLInputElement>(`input[aria-label="${label}"]`)!;
    const save = byText(container, 'パーミッションを保存する');
    expect(box('管理者：マニュアル操作').disabled).toBe(true);
    expect(box('夜勤：すべての画面を見る').disabled).toBe(true);
    expect(box('夜勤：すべての画面を見る').checked).toBe(true);
    expect(save.disabled).toBe(true);

    click(box('夜勤：マニュアル操作'));
    click(save);

    const saved = onSavePermissions.mock.calls[0]![0] as ReadonlyMap<string, ReadonlySet<string>>;
    expect([...saved.get('night')!]).toEqual(['manual']);
    expect(saved.has('admin')).toBe(false);
  });

  it('管理者でなければ、どの操作も使えない', () => {
    const { container } = setup({ permissions: initialRoles.保守 });

    expect(byText(container, 'ユーザーを加える').disabled).toBe(true);
    expect(container.textContent).toContain('管理者のロールで使えます');
  });
});
