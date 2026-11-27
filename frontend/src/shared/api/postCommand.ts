import { screenFetch } from './screenSession';
/** 画面からの操作の結果。応答がない・断られたときは failed（やり直さない）。 */
export type CommandResult = 'ok' | 'failed';

export async function postWithSession(baseUrl: string, path: string, timeoutMs: number, body?: unknown): Promise<Response> {
  const signal = AbortSignal.timeout(timeoutMs);
  const session = await screenFetch(`${baseUrl}/identity/user/session`, { cache: 'no-store', signal });
  if (!session.ok) throw new Error(`ログイン状態を確認できませんでした（${session.status}）。`);
  const { requestToken } = await session.json();
  const headers: Record<string, string> = { 'X-FlowHub-CSRF': requestToken };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  return screenFetch(`${baseUrl}${path}`, { method: 'POST', headers, signal, body: JSON.stringify(body) });
}

/** HTTP の窓口へ POST で送る。応答がない（timeoutMs を過ぎた）・つながらない・断られたときは failed にして、やり直さない。 */
export async function postCommand(baseUrl: string, path: string, timeoutMs: number): Promise<CommandResult> {
  try {
    const response = await postWithSession(baseUrl, path, timeoutMs);
    return response.ok ? 'ok' : 'failed';
  } catch (error) {
    console.warn(`${path} を送れませんでした。`, error);
    return 'failed';
  }
}

/**
 * HTTP の窓口へ JSON を POST で送り、返ってきた `{ "result": "…" }` を受け取る（応答を待つのは timeoutMs まで。やり直さない）。
 * - 409（今の状態では受け付けられない）は refused。
 * - results にない結果、ほかの応答、応答がない・つながらないときは failed（届いたかわからない）。
 */
export async function postOperation<T extends string>(
  baseUrl: string,
  path: string,
  body: unknown,
  timeoutMs: number,
  results: ReadonlySet<T>,
): Promise<T | 'refused' | 'failed'> {
  try {
    const response = await screenFetch(`${baseUrl}${path}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
      signal: AbortSignal.timeout(timeoutMs),
    });
    if (response.status === 409) {
      return 'refused';
    }
    if (!response.ok) {
      console.warn(`${path} は受け付けられませんでした（${response.status}）。`);
      return 'failed';
    }
    const result: unknown = ((await response.json()) as { result?: unknown }).result;
    if (typeof result === 'string' && (results as ReadonlySet<string>).has(result)) {
      return result as T;
    }
    console.warn(`${path} の結果がわかりません（${String(result)}）。`);
    return 'failed';
  } catch (error) {
    console.warn(`${path} を送れませんでした。`, error);
    return 'failed';
  }
}
