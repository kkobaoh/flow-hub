export interface LoginSession {
  readonly userId: string;
  readonly mustChangePassword: boolean;
  readonly minimumLength: number;
  readonly requestToken: string;
}

export class SessionExpired extends Error {}

async function post(path: string, input: object, token: string) {
  return fetch('/identity/user/' + path, {
    method: 'POST', cache: 'no-store',
    headers: { 'Content-Type': 'application/json', 'X-FlowHub-CSRF': token },
    body: JSON.stringify(input),
  });
}

export async function changeOwnPassword(currentPassword: string, password: string, token: string) {
  const response = await post('password', { currentPassword, password }, token);
  if (response.status === 401) throw new SessionExpired('もう一度ログインしてください。');
  if (response.status === 429) throw new Error('ログインがロックされています。時間をおいてやり直してください。');
  if (response.status === 400) {
    const problem = await response.json().catch(() => ({}));
    if (problem.reason === 'incorrect') throw new Error('現在のパスワードが一致しません。');
    if (problem.reason === 'unchanged') throw new Error('現在と異なるパスワードを入力してください。');
    if (problem.reason === 'tooShort') throw new Error(`新しいパスワードは${problem.minimumLength}文字以上で入力してください。`);
  }
  if (!response.ok) throw new Error('パスワードの変更結果を確認できませんでした。');
}

export const userLogin = {
  async session(): Promise<LoginSession | null> {
    const response = await fetch('/identity/user/session', { cache: 'no-store' });
    if (response.status === 401) return null;
    if (!response.ok) throw new Error('ログイン状態を確認できませんでした。');
    return response.json();
  },
  async login(userId: string, password: string) {
    const form = await fetch('/identity/user/form', { cache: 'no-store' });
    if (!form.ok) throw new Error('ログイン画面を開けませんでした。');
    const response = await post('login', { userId, password }, (await form.json()).requestToken);
    if (response.status === 401) throw new Error('ユーザー ID またはパスワードが一致しません。');
    if (response.status === 429) throw new Error('ログインがロックされています。時間をおいてやり直してください。');
    if (!response.ok) throw new Error('ログインできませんでした。');
  },
  async update(password: string, token: string) {
    const response = await post('initial-password', { password }, token);
    if (response.status === 401) throw new SessionExpired('もう一度ログインしてください。');
    if (response.status === 400) {
      const problem = await response.json().catch(() => ({}));
      if (problem.reason === 'unchanged') throw new Error('仮パスワードと異なるパスワードを入力してください。');
      if (problem.reason === 'tooShort') throw new Error(`パスワードは${problem.minimumLength}文字以上で入力してください。`);
    }
    if (!response.ok) throw new Error('パスワードを変更できませんでした。');
  },
  async logout(token: string) {
    const response = await post('logout', {}, token);
    if (!response.ok && response.status !== 401) throw new Error('ログアウトできませんでした。');
  },
};
