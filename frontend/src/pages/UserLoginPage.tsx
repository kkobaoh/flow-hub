import { useEffect, useState } from 'react';
import { changeOwnPassword, SessionExpired, userLogin, type LoginSession } from '../features/auth/userLogin';
import { darkButton, field, section } from '../shared/components/forms';

export function UserLoginPage({ api = userLogin, changeOwn = false }: { readonly api?: typeof userLogin; readonly changeOwn?: boolean }) {
  const [session, setSession] = useState<LoginSession | null>();
  const [id, setId] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [current, setCurrent] = useState('');
  const [changed, setChanged] = useState(false);
  const changing = changeOwn && !changed && session && !session.mustChangePassword;
  useEffect(() => {
    let active = true;
    api.session().then(value => { if (active) setSession(value); })
      .catch(() => { if (active) { setError('ログイン状態を確認できませんでした。'); setSession(null); } });
    return () => { active = false; };
  }, [api]);

  async function submit() {
    if (busy || session === undefined) return;
    setBusy(true);
    setError('');
    try {
      if (!session) await api.login(id, password);
      else if (session.mustChangePassword) await api.update(password, session.requestToken);
      else if (changing) {
        await changeOwnPassword(current, password, session.requestToken);
        setCurrent('');
        setChanged(true);
      } else {
        await api.logout(session.requestToken);
        setChanged(false);
      }
      setPassword('');
      setSession(await api.session());
    } catch (e) {
      if (e instanceof SessionExpired) { setSession(null); setPassword(''); setCurrent(''); }
      setError(e instanceof Error ? e.message : 'サーバーに接続できませんでした。');
    } finally {
      setBusy(false);
    }
  }

  const ready = session && !session.mustChangePassword;
  return (
    <main className="mx-auto max-w-lg px-4 py-12 text-graphite">
      <section className={section}>
        <h1 className="text-xl font-bold">{changing ? '自分のパスワードを変える' : session?.mustChangePassword ? '初回パスワード変更' : 'ログイン'}</h1>
        {session === undefined ? <p>ログイン状態を確認しています。</p> : (
          <form className="flex flex-col gap-3" onSubmit={event => { event.preventDefault(); void submit(); }}>
            {session ? <p>ユーザー ID　{session.userId}</p> : (
              <label className="flex flex-col gap-1">ユーザー ID
                <input className={field} autoComplete="username" disabled={busy} value={id} onChange={e => setId(e.target.value)} />
              </label>
            )}
            {changing && <label className="flex flex-col gap-1">現在のパスワード
              <input type="password" className={field} autoComplete="current-password" disabled={busy} value={current} onChange={e => setCurrent(e.target.value)} />
            </label>}
            {changed && <p role="status">パスワードを変更しました。</p>}
            {ready && !changing ? <a href="/" className="underline">全体の様子を開く</a> : (
              <label className="flex flex-col gap-1">{session ? '新しいパスワード' : 'パスワード'}
                <input type="password" className={field} value={password} disabled={busy}
                  autoComplete={session ? 'new-password' : 'current-password'} onChange={e => setPassword(e.target.value)} />
              </label>
            )}
            {session?.mustChangePassword && <p>仮パスワードと異なる、{session.minimumLength}文字以上のパスワードを入力してください。</p>}
            {changing && <p>現在と異なる、{session.minimumLength}文字以上のパスワードを入力してください。変更後、ほかの端末では再ログインが必要です。</p>}
            {error && <p role="alert">{error}</p>}
            <button className={darkButton} disabled={busy || (!session && !id.trim()) || (!ready && !password) || (!!changing && (!current || !password))}>
              {changing ? '変更する' : ready ? 'ログアウト' : session ? '変更する' : 'ログイン'}
            </button>
          </form>
        )}
      </section>
    </main>
  );
}
