import { useState } from 'react';
import { ConfirmDialog } from '../shared/components/ConfirmDialog';
import { Choice, darkButton, field, FormDialog, FormFooter, note, plainButton, RowActions, section, td, TextField, th } from '../shared/components/forms';
import { byIdNumber } from '../shared/utils/listing';
import { permissionLabels, type Permission, type Permissions } from '../features/auth/permissions';
import { dateTimeOf } from '../shared/utils/dateTime';
import {
  durationLabel,
  durationUnitLabel,
  permissionsChanged,
  roleChangeBlockedReason,
  roleDeleteBlockedReason,
  roleProblem,
  stateLabel,
  userAdminBlockedReason,
  userProblem,
  type Duration,
  type DurationUnit,
  type Role,
  type RoleDraft,
  type UserAccount,
} from '../features/user-management/users';

export interface UsersPageProps {
  readonly users: readonly UserAccount[];
  readonly roles: readonly Role[];
  readonly permissions: Permissions;
  readonly onChangeRole: (user: string, role: string) => void;
  readonly onUnlock: (user: string) => void;
  /** 仮のパスワードにする。システムが作ったパスワードを返す（画面で一度だけ見せる）。 */
  readonly onResetPassword: (user: string) => Promise<string>;
  /** ユーザーを加える（デフォルトのロールを付ける）。システムが作った仮のパスワードを返す。 */
  readonly onAddUser: (user: string) => Promise<string>;
  /** ロールを保存する。original は直すロールの id（加えるときは null）。 */
  readonly onSaveRole: (original: string | null, role: { readonly name: string; readonly timeout: Duration }) => void;
  readonly onDeleteRole: (id: string) => void;
  readonly onMakeDefault: (id: string) => void;
  /** 管理者のロールを除く、ロールごとのパーミッション。 */
  readonly onSavePermissions: (permissions: ReadonlyMap<string, ReadonlySet<Permission>>) => void;
}

type Dialog =
  | { readonly kind: 'addUser' }
  | { readonly kind: 'reset'; readonly user: string }
  | { readonly kind: 'password'; readonly user: string; readonly password: string }
  | { readonly kind: 'failed'; readonly message: string }
  | { readonly kind: 'role'; readonly target: Role | null }
  | { readonly kind: 'deleteRole'; readonly target: Role };

const VIEW_ALL = 'すべての画面を見る';

/** ユーザーとロールの管理（docs/screens.md の「9 ユーザーとロールの管理」）。 */
type DirectoryProps = Pick<UsersPageProps, 'users' | 'roles'> & {
  readonly currentUserId?: string;
  readonly readOnly: true;
  readonly onAddUser?: UsersPageProps['onAddUser'] | undefined;
  readonly onChangeRole?: UsersPageProps['onChangeRole'] | undefined;
  readonly onUnlock?: UsersPageProps['onUnlock'] | undefined;
  readonly onMakeDefault?: UsersPageProps['onMakeDefault'] | undefined;
  readonly onDeleteRole?: UsersPageProps['onDeleteRole'] | undefined;
  readonly onResetPassword?: UsersPageProps['onResetPassword'] | undefined;
  readonly onIssuedClosed?: () => void;
};

export function UsersPage(props: UsersPageProps | DirectoryProps) {
  const { users, roles } = props;
  const actions = 'readOnly' in props ? undefined : props;
  const [tab, setTab] = useState<'users' | 'roles'>('users');
  const [dialog, setDialog] = useState<Dialog | null>(null);
  const [issuing, setIssuing] = useState(false);
  const blocked = actions ? userAdminBlockedReason(actions.permissions) : props.onAddUser
    ? 'ユーザーの追加、ロールの付け替え、ロックの解除ができます。' : 'ユーザーとロールの変更には管理者ロールが必要です。';
  const allowed = blocked === null;
  const deleteRole = !actions || allowed ? props.onDeleteRole : undefined;
  const add = !actions || allowed ? props.onAddUser : undefined;
  const resetPassword = !actions || allowed ? props.onResetPassword : undefined;
  const close = () => {
    if ('readOnly' in props && (dialog?.kind === 'password' || dialog?.kind === 'failed')) props.onIssuedClosed?.();
    setDialog(null);
  };
  const defaultRole = roles.find((r) => r.isDefault);
  const showPassword = async (user: string, made: Promise<string>) => {
    setIssuing(true);
    try {
      setDialog({ kind: 'password', user, password: await made });
    } catch (error) {
      setDialog({ kind: 'failed', message: error instanceof Error ? error.message : '仮のパスワードを取得できませんでした。' });
    } finally {
      setIssuing(false);
    }
  };

  return (
    <>
      {blocked !== null && <p className="m-0 text-sm font-bold">{blocked}</p>}
      <div role="tablist" aria-label="管理するもの" className="flex flex-wrap gap-1 border-b-2 border-line">
        {(
          [
            ['users', 'ユーザー'],
            ['roles', 'ロール'],
          ] as const
        ).map(([key, label]) => (
          <button
            key={key}
            type="button"
            role="tab"
            aria-selected={tab === key}
            onClick={() => setTab(key)}
            className={`-mb-0.5 min-h-12 border-b-3 px-4.5 text-[15px] font-bold focus-visible:outline-3 focus-visible:outline-focus ${tab === key ? 'border-graphite text-graphite' : 'border-transparent text-ink-muted'}`}
          >
            {label}
          </button>
        ))}
      </div>

      {tab === 'users' ? (
        <section aria-label="ユーザー" className={section}>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="m-0 max-w-[70ch] text-sm text-ink-muted">
              新しいユーザーには、デフォルトのロール（{defaultRole?.name ?? '－'}）を付けます。パスワードを忘れた人には、仮のパスワードを設定します（次のログインで変更を求めます）。
            </p>
            <button type="button" disabled={!add || issuing} onClick={() => setDialog({ kind: 'addUser' })} className={plainButton}>
              ユーザーを加える
            </button>
          </div>
          <div className="relative overflow-x-auto">
            <table className="w-full min-w-[860px] border-collapse text-sm">
              <thead>
                <tr>
                  {['ユーザー ID', 'ロール', '最後のログイン', '状態'].map((h) => (
                    <th key={h} scope="col" className={th}>
                      {h}
                    </th>
                  ))}
                  <th scope="col" className={th}>
                    <span className="sr-only">操作</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {[...users]
                  .sort((a, b) => byIdNumber(a.id, b.id))
                  .map((u) => {
                    const rolePermissions = actions?.permissions ?? roles.find(r => r.fixed)?.permissions;
                    const reason = props.onChangeRole && rolePermissions
                      ? roleChangeBlockedReason(u, users, roles, rolePermissions) : blocked;
                    return (
                      <tr key={u.id} className="border-b border-disabled-border">
                        <td className={`${td} font-number text-[17px] font-bold`}>{u.id}</td>
                        <td className={td}>
                          <select
                            aria-label={`${u.id} のロール`}
                            value={u.role}
                            disabled={reason !== null}
                            onChange={(e) => props.onChangeRole?.(u.id, e.target.value)}
                            className={`${field} disabled:bg-ground`}
                          >
                            {roles.map((r) => (
                              <option key={r.id} value={r.id}>
                                {r.name}
                              </option>
                            ))}
                          </select>
                          {reason !== null && (allowed || props.onChangeRole) && <p className={`${note} mt-1`}>{reason}</p>}
                        </td>
                        <td className={`${td} font-number text-base font-medium`}>{u.lastLogin === null ? '－' : dateTimeOf(u.lastLogin)}</td>
                        <td className={`${td} font-bold ${u.state.kind === 'active' ? 'text-running' : 'text-attention'}`}>{stateLabel(u.state)}</td>
                        <td className="px-3 py-1.5">
                          <RowActions reason={null}>
                            {u.state.kind === 'locked' && (
                              <button type="button" disabled={!props.onUnlock || (!!actions && !allowed)} onClick={() => props.onUnlock?.(u.id)} className={darkButton}>
                                ロックを外す
                              </button>
                            )}
                            <button type="button" disabled={!resetPassword || issuing || ('currentUserId' in props && props.currentUserId === u.id)} onClick={() => setDialog({ kind: 'reset', user: u.id })} className={plainButton}>
                              仮のパスワードにする
                            </button>
                          </RowActions>
                        </td>
                      </tr>
                    );
                  })}
              </tbody>
            </table>
          </div>
        </section>
      ) : (
        <>
          <section aria-label="ロール" className={section}>
            <div className="flex flex-wrap items-center justify-between gap-3">
              <p className="m-0 max-w-[70ch] text-sm text-ink-muted">
                管理者のロールは、名前とパーミッションが固定で、消せません。デフォルトのロールは消せません（中身は変えられます）。
              </p>
              <button type="button" disabled={!allowed} onClick={() => setDialog({ kind: 'role', target: null })} className={plainButton}>
                ロールを加える
              </button>
            </div>
            <div className="relative overflow-x-auto">
              <table className="w-full min-w-[860px] border-collapse text-sm">
                <thead>
                  <tr>
                    {['ロール', 'ユーザーの数', '操作がないままログインしておける時間', '印'].map((h) => (
                      <th key={h} scope="col" className={th}>
                        {h}
                      </th>
                    ))}
                    <th scope="col" className={th}>
                      <span className="sr-only">操作</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {roles.map((r) => {
                    const reason = roleDeleteBlockedReason(r, users);
                    return (
                      <tr key={r.id} className="border-b border-disabled-border">
                        <td className={`${td} font-bold`}>{r.name}</td>
                        <td className={`${td} font-number text-[17px] font-semibold`}>{users.filter((u) => u.role === r.id).length}</td>
                        <td className={td}>{durationLabel(r.timeout)}</td>
                        <td className={td}>{[r.isDefault ? 'デフォルト' : '', r.fixed ? '固定' : ''].filter((m) => m !== '').join('、')}</td>
                        <td className="px-3 py-1.5">
                          <RowActions reason={deleteRole && !r.fixed && !r.isDefault ? reason : null}>
                            {!r.isDefault && !r.fixed && (
                              <button type="button" disabled={!props.onMakeDefault || (!!actions && !allowed)} onClick={() => props.onMakeDefault?.(r.id)} className={plainButton}>
                                デフォルトにする
                              </button>
                            )}
                            <button type="button" disabled={!allowed} onClick={() => setDialog({ kind: 'role', target: r })} className={plainButton}>
                              変える
                            </button>
                            <button type="button" disabled={!deleteRole || reason !== null} onClick={() => setDialog({ kind: 'deleteRole', target: r })} className={plainButton}>
                              消す
                            </button>
                          </RowActions>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </section>
          <PermissionMatrix key={roles.map((r) => r.id).join()} roles={roles} allowed={allowed} onSave={actions?.onSavePermissions} />
        </>
      )}

      {add && dialog?.kind === 'addUser' && (
        <AddUserDialog
          existing={users.map((u) => u.id)}
          defaultRole={defaultRole?.name ?? '－'}
          onSave={(id) => {
            close();
            void showPassword(id, add(id));
          }}
          onCancel={close}
        />
      )}
      {resetPassword && dialog?.kind === 'reset' && (
        <ConfirmDialog
          title={`${dialog.user} を仮のパスワードにしますか？`}
          body="現在のパスワードは使えなくなり、すべての端末で再ログインが必要になります。新しい仮パスワードは一度だけ表示します。ログインの失敗回数とロックは引き継ぎます。"
          confirmLabel="仮のパスワードにする"
          tone="danger"
          onConfirm={() => {
            const user = dialog.user;
            close();
            void showPassword(user, resetPassword(user));
          }}
          onCancel={close}
        />
      )}
      {dialog?.kind === 'password' && (
        <FormDialog title={`${dialog.user} の仮のパスワード`} onCancel={close}>
          <p className={note}>この画面を閉じると、もう見られません。本人に伝えてください。次のログインで、パスワードの変更を求めます。</p>
          <p className="m-0 rounded-lg border border-control-border bg-white px-4 py-3 text-center font-number text-[24px] font-bold tracking-wider select-all">{dialog.password}</p>
          <div className="flex justify-end">
            <button type="button" onClick={close} className={darkButton}>
              閉じる
            </button>
          </div>
        </FormDialog>
      )}
      {dialog?.kind === 'failed' && (
        <FormDialog title="仮のパスワードを作れませんでした" onCancel={close}>
          <p className="m-0 text-[15px]">{dialog.message}</p>
          <div className="flex justify-end">
            <button type="button" onClick={close} className={plainButton}>
              閉じる
            </button>
          </div>
        </FormDialog>
      )}
      {dialog?.kind === 'role' && (
        <RoleDialog
          target={dialog.target}
          roles={roles}
          onSave={(role) => {
            actions?.onSaveRole(dialog.target?.id ?? null, role);
            close();
          }}
          onCancel={close}
        />
      )}
      {dialog?.kind === 'deleteRole' && (
        <ConfirmDialog
          title={`${dialog.target.name} を消しますか？`}
          body="このロールを消します。消したロールは、元に戻せません。"
          confirmLabel="ロールを消す"
          tone="danger"
          onConfirm={() => {
            deleteRole?.(dialog.target.id);
            close();
          }}
          onCancel={close}
        />
      )}
    </>
  );
}

function PermissionMatrix({
  roles,
  allowed,
  onSave,
}: {
  readonly roles: readonly Role[];
  readonly allowed: boolean;
  readonly onSave: UsersPageProps['onSavePermissions'] | undefined;
}) {
  const editable = roles.filter((r) => !r.fixed);
  const initial = new Map(editable.map((r) => [r.id, new Set(r.permissions)] as const));
  const [draft, setDraft] = useState<ReadonlyMap<string, ReadonlySet<Permission>>>(initial);
  const changed = permissionsChanged(initial, draft);
  const toggle = (role: string, p: Permission) =>
    setDraft((d) => {
      const next = new Map(d);
      const set = new Set(d.get(role));
      if (set.has(p)) {
        set.delete(p);
      } else {
        set.add(p);
      }
      next.set(role, set);
      return next;
    });
  const has = (r: Role, p: Permission) => (r.fixed ? r.permissions.has(p) : (draft.get(r.id)?.has(p) ?? false));

  return (
    <section aria-labelledby="perm-title" className={section}>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 id="perm-title" className="m-0 text-lg font-bold">
          ロールごとのパーミッション
        </h2>
        <button type="button" disabled={!allowed || !changed} onClick={() => onSave?.(draft)} className={darkButton}>
          パーミッションを保存する
        </button>
      </div>
      <div className="relative overflow-x-auto">
        <table className="w-full min-w-[760px] border-collapse text-sm">
          <thead>
            <tr>
              <th scope="col" className={th}>
                パーミッション
              </th>
              {roles.map((r) => (
                <th key={r.id} scope="col" className={`${th} text-center`}>
                  {r.name}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            <tr className="border-b border-disabled-border">
              <th scope="row" className="px-3 py-2 text-left font-normal">
                {VIEW_ALL}
              </th>
              {roles.map((r) => (
                <td key={r.id} className="px-3 py-2 text-center">
                  <input type="checkbox" checked disabled aria-label={`${r.name}：${VIEW_ALL}`} className="size-5 accent-graphite" />
                </td>
              ))}
            </tr>
            {permissionLabels.map(([p, label]) => (
              <tr key={p} className="border-b border-disabled-border">
                <th scope="row" className="px-3 py-2 text-left font-normal">
                  {label}
                </th>
                {roles.map((r) => (
                  <td key={r.id} className="px-3 py-2 text-center">
                    <input
                      type="checkbox"
                      checked={has(r, p)}
                      disabled={!allowed || r.fixed || p === 'userAdmin'}
                      onChange={() => toggle(r.id, p)}
                      aria-label={`${r.name}：${label}`}
                      className="size-5 accent-graphite"
                    />
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

function AddUserDialog({ existing, defaultRole, onSave, onCancel }: { readonly existing: readonly string[]; readonly defaultRole: string; readonly onSave: (id: string) => void; readonly onCancel: () => void }) {
  const [id, setId] = useState('');
  return (
    <FormDialog title="ユーザーを加える" onCancel={onCancel}>
      <p className={note}>デフォルトのロール（{defaultRole}）を付けます。仮のパスワードはシステムが作り、加えたあとに一度だけ見せます。</p>
      <TextField label="ユーザー ID" value={id} onChange={setId} />
      <FormFooter problem={userProblem(id, existing)} saveLabel="ユーザーを加える" onCancel={onCancel} onSave={() => onSave(id.trim())} />
    </FormDialog>
  );
}

function RoleDialog({
  target,
  roles,
  onSave,
  onCancel,
}: {
  readonly target: Role | null;
  readonly roles: readonly Role[];
  readonly onSave: (role: { readonly name: string; readonly timeout: Duration }) => void;
  readonly onCancel: () => void;
}) {
  const [draft, setDraft] = useState<RoleDraft>(
    target === null ? { name: '', amount: '8', unit: 'hours' } : { name: target.name, amount: String(target.timeout.amount), unit: target.timeout.unit },
  );
  const problem = roleProblem(draft, roles, target?.id ?? null);
  return (
    <FormDialog title={target === null ? 'ロールを加える' : `${target.name} を変える`} onCancel={onCancel}>
      {target === null && <p className={note}>加えたロールは「{VIEW_ALL}」だけから始めます。操作のパーミッションは、ロールごとのパーミッションの表で許してください。</p>}
      {target?.fixed !== true && <TextField label="ロールの名前" value={draft.name} onChange={(name) => setDraft((d) => ({ ...d, name }))} />}
      <div className="flex items-end gap-2">
        <div className="flex-1">
          <TextField label="ログインしておける時間" numeric value={draft.amount} onChange={(amount) => setDraft((d) => ({ ...d, amount }))} />
        </div>
        <Choice
          label="単位"
          unselected={false}
          value={draft.unit}
          options={(Object.keys(durationUnitLabel) as DurationUnit[]).map((u) => [u, durationUnitLabel[u]] as const)}
          onChange={(unit) => setDraft((d) => ({ ...d, unit: unit as DurationUnit }))}
        />
      </div>
      <FormFooter problem={problem} saveLabel="ロールを保存する" onCancel={onCancel} onSave={() => onSave({ name: draft.name.trim(), timeout: { amount: Number(draft.amount), unit: draft.unit } })} />
    </FormDialog>
  );
}
