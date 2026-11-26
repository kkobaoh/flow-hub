import { useState } from 'react';
import { darkButton, field, section } from '../shared/components/forms';
import { FirstAdministratorPage } from './FirstAdministratorPage';

async function loginSetup(password: string) {
  const form = await fetch('/identity/setup/login-form', { cache: 'no-store' });
  if (!form.ok) throw new Error('初期設定画面を開けませんでした。');
  const { requestToken } = await form.json();
  const response = await fetch('/identity/setup/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-FlowHub-CSRF': requestToken },
    body: JSON.stringify({ password }),
  });
  if (response.ok) return;
  if (response.status === 409) throw new Error('初期設定はすでに完了しています。');
  if (response.status === 429) throw new Error('ログインはロックされています。時間をおいてやり直してください。');
  if (response.status === 401) throw new Error('パスワードが一致しません。');
  throw new Error('ログインできませんでした。');
}

export function SetupPage({ login = loginSetup }: { readonly login?: (password: string) => Promise<void> }) {
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [authenticated, setAuthenticated] = useState(false);
  const [error, setError] = useState('');
  async function submit() {
    if (busy || !password) return;
    setBusy(true);
    setError('');
    try {
      await login(password);
      setPassword('');
      setAuthenticated(true);
    } catch (e) {
      setError(e instanceof Error ? e.message : '通信に失敗しました。');
    } finally {
      setBusy(false);
    }
  }
  if (authenticated) return <FirstAdministratorPage />;
  return (
    <main className="mx-auto max-w-lg px-4 py-12 text-graphite">
      <section className={section}>
        <h1 className="text-xl font-bold">初期設定</h1>
        <p>初期設定専用の setup でログインします。</p>
        <form className="flex flex-col gap-3" onSubmit={(event) => { event.preventDefault(); void submit(); }}>
          <label className="flex flex-col gap-1">設置先のパスワード
            <input className={field} type="password" autoComplete="current-password" value={password}
              onChange={(event) => setPassword(event.target.value)} disabled={busy} />
          </label>
          {error && <p role="alert">{error}</p>}
          <button className={darkButton} disabled={!password || busy}>ログイン</button>
        </form>
      </section>
    </main>
  );
}
