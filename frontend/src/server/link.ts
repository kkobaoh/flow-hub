import type { AgvId } from '../map/agv';
import type { AgvCommand, AgvCommandResult } from './agvCommands';
import type { AgvsSnapshot } from './agvs';
import type { MessagesSnapshot } from './messages';
import type { SystemStatusSnapshot } from './systemStatus';

/**
 * サーバーとのつながりの様子。
 * - connecting：初めてつなごうとしている（つながるまで、間を空けて試し続ける）。
 * - connected：つながっている。
 * - reconnecting：切れたので、つなぎ直している。
 */
export type ConnectionState = 'connecting' | 'connected' | 'reconnecting';

/** 画面からの操作の結果。応答がない・断られたときは failed（やり直さない）。 */
export type CommandResult = 'ok' | 'failed';

/** サーバーから届くものを受け取る。 */
export interface ServerHandlers {
  readonly onConnection: (state: ConnectionState) => void;
  readonly onSystemStatus: (snapshot: SystemStatusSnapshot) => void;
  readonly onAgvs: (snapshot: AgvsSnapshot) => void;
  readonly onMessages: (snapshot: MessagesSnapshot) => void;
}

/**
 * 画面とサーバーのつながり（ポート）。本物は SignalR と HTTP（signalRLink.ts）、テストと Storybook は偽物（fakeLink.ts）。
 * - 様子は、サーバーから SignalR で届く（つないだとき、変わったとき）。
 * - 操作は、HTTP の窓口へ送る。
 */
export interface ServerLink {
  /** つないで、届いたものを渡す。返した関数で切る。 */
  connect(handlers: ServerHandlers): () => void;
  /** 運転状態をスタートにする。 */
  startSystem(): Promise<CommandResult>;
  /** 運転状態を停止にする。 */
  stopSystem(): Promise<CommandResult>;
  /** AGV へ操作を送る（応答がなければ、あきらめて failed。やり直さない）。 */
  operateAgv(id: AgvId, command: AgvCommand): Promise<AgvCommandResult>;
}

/** 操作を送って、応答を待つ長さ。 */
export const COMMAND_TIMEOUT_MS = 10_000;

/** つなぎ直すまでの間（それまでに試した回数 → ミリ秒）。すぐ、2秒、5秒、そのあとは 10 秒ごとに、ずっと続ける。 */
export function retryDelayMs(previousRetryCount: number): number {
  const delays = [0, 2000, 5000];
  return delays[previousRetryCount] ?? 10_000;
}

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
