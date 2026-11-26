import { useEffect, useState } from 'react';
import { addUser, fetchUserDirectory, DirectoryAccessError, type UserDirectory } from '../features/user-management/userDirectory';
import { plainButton } from '../shared/components/forms';
import { UsersPage } from './UsersPage';

type View = { kind: 'loading' | 'login' | 'failed' } | { kind: 'ready'; data: UserDirectory };

export function UserDirectoryPage({ load = fetchUserDirectory }: { readonly load?: typeof fetchUserDirectory }) {
  const [view, setView] = useState<View>({ kind: 'loading' });
  const [attempt, setAttempt] = useState(0);
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
    case 'ready': return <UsersPage readOnly {...view.data}
      onAddUser={view.data.canAddUser ? addUser : undefined} onIssuedClosed={() => setAttempt(n => n + 1)} />;
  }
}
