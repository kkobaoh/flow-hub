import { useState } from 'react';
import { darkButton, field, section } from '../shared/components/forms';
import { userProblem } from '../features/user-management/users';

async function createAdministrator(userId: string): Promise<string> {
  const form = await fetch('/identity/setup/form', { cache: 'no-store' });
  if (!form.ok) throw setupError(form.status);
  const { requestToken } = await form.json();
  const response = await fetch('/identity/setup/administrator', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-FlowHub-CSRF': requestToken },
    body: JSON.stringify({ userId }),
  });
  if (!response.ok) throw setupError(response.status);
  return (await response.json()).temporaryPassword;
}

function setupError(status: number) {
  if (status === 401 || status === 403) return new Error('初期設定専用アカウントでログインしてください。');
  if (status === 409) return new Error('最初の管理者はすでに作成されています。');
  return new Error('管理者の作成結果を確認できませんでした。');
}

export function FirstAdministratorPage({ create = createAdministrator }: { readonly create?: (id: string) => Promise<string> }) {
  const [id, setId] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [result, setResult] = useState<{ password: string } | 'closed'>();
  const problem = userProblem(id, []);
  async function submit() {
    if (busy || problem || result) return;
    setBusy(true);
    setError('');
    try {
      setResult({ password: await create(id.trim()) });
    } catch (e) {
      setError(e instanceof Error ? e.message : '通信に失敗しました。');
    } finally {
      setBusy(false);
    }
  }
  return (
    <main className="mx-auto max-w-lg px-4 py-12 text-graphite">
      <section className={section}>
        <h1 className="text-xl font-bold">最初の管理者の作成</h1>
        {result === 'closed' ? <><p>初期設定が完了しました。</p><a href="/login" className="underline">管理者としてログインする</a></> : result ? (
          <>
            <p>{id.trim()} の仮パスワードです。閉じると再表示できません。</p>
            <output className="break-all rounded-lg bg-white p-3 font-mono">{result.password}</output>
            <button type="button" className={darkButton} onClick={() => setResult('closed')}>閉じる</button>
          </>
        ) : (
          <form className="flex flex-col gap-3" onSubmit={(event) => { event.preventDefault(); void submit(); }}>
            <label className="flex flex-col gap-1">管理者 ID
              <input className={field} value={id} onChange={(event) => setId(event.target.value)} disabled={busy} autoComplete="off" />
            </label>
            {id && problem && <p>{problem}</p>}
            {error && <p role="alert">{error}</p>}
            <button className={darkButton} disabled={busy || !!problem}>管理者を作成する</button>
          </form>
        )}
      </section>
    </main>
  );
}
