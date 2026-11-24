/** 画面からの操作の結果。応答がない・断られたときは failed（やり直さない）。 */
export type CommandResult = 'ok' | 'failed';

/** HTTP の窓口へ POST で送る。応答がない（timeoutMs を過ぎた）・つながらない・断られたときは failed にして、やり直さない。 */
export async function postCommand(baseUrl: string, path: string, timeoutMs: number): Promise<CommandResult> {
  try {
    const response = await fetch(`${baseUrl}${path}`, { method: 'POST', signal: AbortSignal.timeout(timeoutMs) });
    return response.ok ? 'ok' : 'failed';
  } catch (error) {
    console.warn(`${path} を送れませんでした。`, error);
    return 'failed';
  }
}
