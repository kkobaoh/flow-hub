import { useEffect, useReducer, useState } from 'react';
import type { ConnectionState, ServerLink } from './link';
import type { AgvsSnapshot } from '../../features/fleet/api/agvSnapshots';
import type { MapSnapshot } from '../../features/routing/api/mapSnapshot';
import type { EquipmentStatusSnapshot } from '../../features/tool/api/equipmentStatus';
import type { MessagesSnapshot } from '../../features/alarm/api/messages';
import { receiveStatus, type StatusView } from '../../features/system/systemStatus';
import { receiveSnapshot, type SnapshotInput, type SnapshotView } from '../../shared/realtime/versioned';

/** 画面が持つ、サーバーとのつながりと、届いた様子。 */
export interface ServerView {
  readonly connection: ConnectionState;
  readonly status: StatusView;
  readonly agvs: SnapshotView<AgvsSnapshot>;
  readonly messages: SnapshotView<MessagesSnapshot>;
  readonly map: SnapshotView<MapSnapshot>;
  readonly equipment: SnapshotView<EquipmentStatusSnapshot>;
}

const initialStatus: StatusView = { kind: 'waiting' };
const initialAgvs: SnapshotView<AgvsSnapshot> = { kind: 'waiting' };
const receiveAgvs = (view: SnapshotView<AgvsSnapshot>, input: SnapshotInput<AgvsSnapshot>) => receiveSnapshot(view, input);
const initialMessages: SnapshotView<MessagesSnapshot> = { kind: 'waiting' };
const receiveMessages = (view: SnapshotView<MessagesSnapshot>, input: SnapshotInput<MessagesSnapshot>) => receiveSnapshot(view, input);
const initialMap: SnapshotView<MapSnapshot> = { kind: 'waiting' };
const receiveMap = (view: SnapshotView<MapSnapshot>, input: SnapshotInput<MapSnapshot>) => receiveSnapshot(view, input);
const initialEquipment: SnapshotView<EquipmentStatusSnapshot> = { kind: 'waiting' };
const receiveEquipment = (view: SnapshotView<EquipmentStatusSnapshot>, input: SnapshotInput<EquipmentStatusSnapshot>) => receiveSnapshot(view, input);

/**
 * サーバーとつなぎ、つながりの様子と、届いた様子（運転状態、AGV、メッセージ、マップ）を持つ。部品を消したら切る。
 * 切れたら最後の様子を古いものにし、つなぎ直したら届いた様子で差し替える（systemStatus.ts の receiveStatus）。
 */
export function useServer(link: ServerLink): ServerView {
  const [connection, setConnection] = useState<ConnectionState>('connecting');
  const [status, dispatch] = useReducer(receiveStatus, initialStatus);
  const [agvs, dispatchAgvs] = useReducer(receiveAgvs, initialAgvs);
  const [messages, dispatchMessages] = useReducer(receiveMessages, initialMessages);
  const [map, dispatchMap] = useReducer(receiveMap, initialMap);
  const [equipment, dispatchEquipment] = useReducer(receiveEquipment, initialEquipment);

  useEffect(
    () =>
      link.connect({
        onConnection: (state) => {
          setConnection(state);
          if (state !== 'connected') {
            dispatch({ type: 'disconnected' });
            dispatchAgvs({ type: 'disconnected' });
            dispatchMessages({ type: 'disconnected' });
            dispatchMap({ type: 'disconnected' });
            dispatchEquipment({ type: 'disconnected' });
          }
        },
        onSystemStatus: (snapshot) => dispatch({ type: 'snapshot', snapshot }),
        onAgvs: (snapshot) => dispatchAgvs({ type: 'snapshot', snapshot }),
        onMessages: (snapshot) => dispatchMessages({ type: 'snapshot', snapshot }),
        onMap: (snapshot) => dispatchMap({ type: 'snapshot', snapshot }),
        onEquipment: (snapshot) => dispatchEquipment({ type: 'snapshot', snapshot }),
      }),
    [link],
  );

  return { connection, status, agvs, messages, map, equipment };
}
