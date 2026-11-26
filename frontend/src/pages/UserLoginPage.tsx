import { useEffect, useState } from 'react';
import { SessionExpired, userLogin, type LoginSession } from '../features/auth/userLogin';
import { darkButton, field, section } from '../shared/components/forms';

export function UserLoginPage({ api = userLogin }: { readonly api?: typeof userLogin }) {
  const [session, setSession] = useState<LoginSession | null>();
  const [id, setId] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
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
      else await api.logout(session.requestToken);
      setPassword('');
      setSession(await api.session());
    } catch (e) {
      if (e instanceof SessionExpired) { setSession(null); setPassword(''); }
      setError(e instanceof Error ? e.message : 'サーバーに接続できませんでした。');
    } finally {
      setBusy(false);
    }
  }

  const ready = session && !session.mustChangePassword;
  return (
    <main className="mx-auto max-w-lg px-4 py-12 text-graphite">
      <section className={section}>
        <h1 className="text-xl font-bold">{session?.mustChangePassword ? '初回パスワード変更' : 'ログイン'}</h1>
        {session === undefined ? <p>ログイン状態を確認しています。</p> : (
          <form className="flex flex-col gap-3" onSubmit={event => { event.preventDefault(); void submit(); }}>
            {session ? <p>ユーザー ID　{session.userId}</p> : (
              <label className="flex flex-col gap-1">ユーザー ID
                <input className={field} autoComplete="username" disabled={busy} value={id} onChange={e => setId(e.target.value)} />
              </label>
            )}
            {ready ? <a href="/" className="underline">全体の様子を開く</a> : (
              <label className="flex flex-col gap-1">{session ? '新しいパスワード' : 'パスワード'}
                <input type="password" className={field} value={password} disabled={busy}
                  autoComplete={session ? 'new-password' : 'current-password'} onChange={e => setPassword(e.target.value)} />
              </label>
            )}
            {session?.mustChangePassword && <p>仮パスワードと異なる、{session.minimumLength}文字以上のパスワードを入力してください。</p>}
            {error && <p role="alert">{error}</p>}
            <button className={darkButton} disabled={busy || (!session && !id.trim()) || (!ready && !password)}>
              {ready ? 'ログアウト' : session ? '変更する' : 'ログイン'}
            </button>
          </form>
        )}
      </section>
    </main>
  );
}
