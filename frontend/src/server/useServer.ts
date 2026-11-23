import { useEffect, useReducer, useState } from 'react';
import type { ConnectionState, ServerLink } from './link';
import type { AgvsSnapshot } from './agvs';
import { receiveStatus, type StatusView } from './systemStatus';
import { receiveSnapshot, type SnapshotInput, type SnapshotView } from './versioned';

/** 画面が持つ、サーバーとのつながりと、届いた様子。 */
export interface ServerView {
  readonly connection: ConnectionState;
  readonly status: StatusView;
  readonly agvs: SnapshotView<AgvsSnapshot>;
}

const initialStatus: StatusView = { kind: 'waiting' };
const initialAgvs: SnapshotView<AgvsSnapshot> = { kind: 'waiting' };
const receiveAgvs = (view: SnapshotView<AgvsSnapshot>, input: SnapshotInput<AgvsSnapshot>) => receiveSnapshot(view, input);

/**
 * サーバーとつなぎ、つながりの様子と、届いた様子（運転状態、AGV）を持つ。部品を消したら切る。
 * 切れたら最後の様子を古いものにし、つなぎ直したら届いた様子で差し替える（systemStatus.ts の receiveStatus）。
 */
export function useServer(link: ServerLink): ServerView {
  const [connection, setConnection] = useState<ConnectionState>('connecting');
  const [status, dispatch] = useReducer(receiveStatus, initialStatus);
  const [agvs, dispatchAgvs] = useReducer(receiveAgvs, initialAgvs);

  useEffect(
    () =>
      link.connect({
        onConnection: (state) => {
          setConnection(state);
          if (state !== 'connected') {
            dispatch({ type: 'disconnected' });
            dispatchAgvs({ type: 'disconnected' });
          }
        },
        onSystemStatus: (snapshot) => dispatch({ type: 'snapshot', snapshot }),
        onAgvs: (snapshot) => dispatchAgvs({ type: 'snapshot', snapshot }),
      }),
    [link],
  );

  return { connection, status, agvs };
}
