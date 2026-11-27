import { useEffect, useState } from 'react';
import { saveRole, addUser, changeUserRole, deleteRole, resetUserPassword, setDefaultRole, unlockUser, fetchUserDirectory, DirectoryAccessError, type UserDirectory } from '../features/user-management/userDirectory';
import { plainButton } from '../shared/components/forms';
import { UsersPage } from './UsersPage';

type View = { kind: 'loading' | 'login' | 'failed' } | { kind: 'ready'; data: UserDirectory };

export function UserDirectoryPage({ load = fetchUserDirectory }: { readonly load?: typeof fetchUserDirectory }) {
  const [view, setView] = useState<View>({ kind: 'loading' });
  const [attempt, setAttempt] = useState(0);
  const [errorMessage, setErrorMessage] = useState('');
  async function update(operation: Promise<void>) {
    setErrorMessage('');
    try {
      await operation;
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : '変更結果を確認できませんでした。');
    } finally {
      setAttempt(n => n + 1);
    }
  }
  useEffect(() => {
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout> | undefined;
    load(controller.signal).then(data => {
      if (controller.signal.aborted) return;
      setView({ kind: 'ready', data });
      if (data.refreshAfterMs !== undefined) {
        timer = setTimeout(() => setAttempt(n => n + 1), Math.min(data.refreshAfterMs, 2_147_483_647));
      }
    }).catch((error: unknown) => {
      if (!controller.signal.aborted) setView({ kind: error instanceof DirectoryAccessError ? 'login' : 'failed' });
    });
    return () => { controller.abort(); clearTimeout(timer); };
  }, [load, attempt]);
  switch (view.kind) {
    case 'loading': return <p>ユーザーとロールを読み込んでいます。</p>;
    case 'login': return <p>一覧を見るには通常ログインが必要です。<a className="underline" href="/login">ログイン画面へ</a></p>;
    case 'failed': return <div role="alert"><p>ユーザーとロールを読み込めませんでした。</p>
      <button className={plainButton} onClick={() => { setView({ kind: 'loading' }); setAttempt(n => n + 1); }}>読み直す</button></div>;
    case 'ready': return <>{errorMessage && <p role="alert">{errorMessage}</p>}<UsersPage readOnly {...view.data}
      onSaveRole={view.data.canAddUser ? async (id, role) => {
        await saveRole(id, role);
        setAttempt(n => n + 1);
      } : undefined}
      onResetPassword={view.data.canAddUser ? resetUserPassword : undefined}
      onMakeDefault={view.data.canAddUser ? role => update(setDefaultRole(role)) : undefined}
      onDeleteRole={view.data.canAddUser ? role => update(deleteRole(role)) : undefined}
      onChangeRole={view.data.canAddUser ? (user, role) => update(changeUserRole(user, role)) : undefined}
      onUnlock={view.data.canAddUser ? user => {
        const state = view.data.users.find(account => account.id === user)?.state;
        if (state?.kind === 'locked' && state.lockToken) void update(unlockUser(user, state.lockToken));
      } : undefined}
      onAddUser={view.data.canAddUser ? addUser : undefined} onIssuedClosed={() => setAttempt(n => n + 1)} /></>;
  }
}
