import { useEffect, useReducer, useState } from 'react';
import type { ConnectionState, ServerLink } from './link';
import { receiveStatus, type StatusView } from './systemStatus';

/** 画面が持つ、サーバーとのつながりと、届いた様子。 */
export interface ServerView {
  readonly connection: ConnectionState;
  readonly status: StatusView;
}

const initialStatus: StatusView = { kind: 'waiting' };

/**
 * サーバーとつなぎ、つながりの様子と、届いた運転状態の様子を持つ。部品を消したら切る。
 * 切れたら最後の様子を古いものにし、つなぎ直したら届いた様子で差し替える（systemStatus.ts の receiveStatus）。
 */
export function useServer(link: ServerLink): ServerView {
  const [connection, setConnection] = useState<ConnectionState>('connecting');
  const [status, dispatch] = useReducer(receiveStatus, initialStatus);

  useEffect(
    () =>
      link.connect({
        onConnection: (state) => {
          setConnection(state);
          if (state !== 'connected') {
            dispatch({ type: 'disconnected' });
          }
        },
        onSystemStatus: (snapshot) => dispatch({ type: 'snapshot', snapshot }),
      }),
    [link],
  );

  return { connection, status };
}
