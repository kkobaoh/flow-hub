import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { postCommand } from '../../shared/api/postCommand';
import { COMMAND_TIMEOUT_MS, retryDelayMs, type ServerHandlers, type ServerLink } from './link';
import { sendAgvCommand } from '../../features/fleet/api/agvCommands';
import { parseAgvs } from '../../features/fleet/api/agvSnapshots';
import { parseMap } from '../../features/routing/api/mapSnapshot';
import { parseMessages } from '../../features/alarm/api/messages';
import { parseSystemStatus } from '../../features/system/systemStatus';

/** 運転状態の様子を受け取る名前（サーバーの ScreenFeed.SystemStatusMethod と同じ）。 */
const SYSTEM_STATUS = 'systemStatus';

/** AGV の様子を受け取る名前（サーバーの ScreenFeed.AgvsMethod と同じ）。 */
const AGVS = 'agvs';

/** メッセージの様子を受け取る名前（サーバーの ScreenFeed.MessagesMethod と同じ）。 */
const MESSAGES = 'messages';

/** マップの様子を受け取る名前（サーバーの ScreenFeed.MapMethod と同じ）。 */
const MAP = 'map';

/**
 * 本物のつながり。SignalR のハブ（/hubs/screen）で様子を受け取り、操作は HTTP の窓口へ送る。
 * 切れたら、間を空けてずっとつなぎ直す。初めてつなぐときに失敗しても、同じように試し続ける。
 * @param baseUrl サーバーの場所。開発用のサーバー（Vite）では、プロキシを通すので空にする。
 */
export function createSignalRLink(baseUrl = ''): ServerLink {
  return {
    connect: (handlers) => connect(baseUrl, handlers),
    startSystem: () => postCommand(baseUrl, '/system/start', COMMAND_TIMEOUT_MS),
    stopSystem: () => postCommand(baseUrl, '/system/stop', COMMAND_TIMEOUT_MS),
    operateAgv: (id, command) => sendAgvCommand(baseUrl, id, command, COMMAND_TIMEOUT_MS),
  };
}

function connect(baseUrl: string, handlers: ServerHandlers): () => void {
  const connection = new HubConnectionBuilder()
    .withUrl(`${baseUrl}/hubs/screen`)
    .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (context) => retryDelayMs(context.previousRetryCount) })
    .configureLogging(LogLevel.Warning)
    .build();
  let closed = false;
  let timer: ReturnType<typeof setTimeout> | undefined;

  const start = (attempt: number) => {
    connection.start().then(
      () => {
        if (!closed) {
          handlers.onConnection('connected');
        }
      },
      (error: unknown) => {
        if (closed) {
          return;
        }
        console.warn('サーバーとつなげませんでした。間を空けて、つなぎ直します。', error);
        timer = setTimeout(() => start(attempt + 1), retryDelayMs(attempt + 1));
      },
    );
  };

  connection.on(SYSTEM_STATUS, (value: unknown) => {
    try {
      handlers.onSystemStatus(parseSystemStatus(value));
    } catch (error) {
      // 形の正しくない様子は使わず、今の様子のままにする
      console.error('サーバーから届いた運転状態の様子を使えませんでした。', error);
    }
  });
  connection.on(AGVS, (value: unknown) => {
    try {
      handlers.onAgvs(parseAgvs(value));
    } catch (error) {
      // 形の正しくない様子は使わず、今の様子のままにする
      console.error('サーバーから届いた AGV の様子を使えませんでした。', error);
    }
  });
  connection.on(MESSAGES, (value: unknown) => {
    try {
      handlers.onMessages(parseMessages(value));
    } catch (error) {
      // 形の正しくない様子は使わず、今の様子のままにする
      console.error('サーバーから届いたメッセージの様子を使えませんでした。', error);
    }
  });
  connection.on(MAP, (value: unknown) => {
    try {
      handlers.onMap(parseMap(value));
    } catch (error) {
      // 形の正しくない様子は使わず、今の様子のままにする
      console.error('サーバーから届いたマップの様子を使えませんでした。', error);
    }
  });
  connection.onreconnecting(() => handlers.onConnection('reconnecting'));
  connection.onreconnected(() => handlers.onConnection('connected'));
  connection.onclose(() => {
    // 自動のつなぎ直しはずっと続けるので、ここに来るのは、こちらから切ったときか、つなぎ直しが止まったときだけ
    if (!closed) {
      handlers.onConnection('reconnecting');
      start(0);
    }
  });

  handlers.onConnection('connecting');
  // つなぎ始めは、ひと呼吸おく。描いてすぐ消された（React の StrictMode など）ときに、交渉の途中で切らないため
  timer = setTimeout(() => start(0), 0);

  return () => {
    closed = true;
    clearTimeout(timer);
    void connection.stop();
  };
}
