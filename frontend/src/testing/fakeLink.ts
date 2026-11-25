import type { CommandResult } from '../shared/api/postCommand';
import type { ConnectionState, ServerHandlers, ServerLink } from '../app/realtime/link';
import type { AgvId } from '../features/fleet/agv';
import type { AgvCommand, AgvCommandResult } from '../features/fleet/api/agvCommands';
import type { AgvsSnapshot } from '../features/fleet/api/agvSnapshots';
import type { MapSnapshot } from '../features/routing/api/mapSnapshot';
import type { EquipmentStatusSnapshot } from '../features/tool/api/equipmentStatus';
import type { TransportsSnapshot } from '../features/dispatch/api/transportsSnapshot';
import type { ConsumablesSnapshot } from '../features/tool/api/consumablesSnapshot';
import type { TransportCancelResult } from '../features/dispatch/api/transportCommands';
import type { ConsumableCommand, ConsumableCommandResult } from '../features/tool/api/consumableCommands';
import type { MessagesSnapshot } from '../features/alarm/api/messages';
import type { SystemStatusSnapshot } from '../features/system/systemStatus';

type Command = 'start' | 'stop';

/** テストと Storybook で使う、偽物のつながり。届くものを手で送り、送られた操作を覚える。 */
export interface FakeLink extends ServerLink {
  /** 送られた操作。 */
  readonly sent: readonly Command[];
  /** AGV へ送られた操作。 */
  readonly agvCommands: readonly { readonly id: AgvId; readonly command: AgvCommand }[];
  /** AGV への操作の結果を決める（既定は accepted）。holdResponses で止めておける。 */
  respondToAgv(result: AgvCommandResult): void;
  /** 消耗品へ送られた操作。 */
  readonly consumableCommands: readonly ConsumableCommand[];
  /** 消耗品の操作の結果を決める（既定は、受け付けたときの結果）。holdResponses で止めておける。 */
  respondToConsumables(result: ConsumableCommandResult): void;
  /** 今つないでいるか。 */
  readonly connected: boolean;
  /** 操作の結果を決める（既定は ok）。 */
  respondWith(result: CommandResult): void;
  /** 操作の結果を、release を呼ぶまで返さずにおく。 */
  holdResponses(): void;
  /** 返さずにおいた操作の結果を返す。 */
  release(): void;
  emitConnection(state: ConnectionState): void;
  emitSystemStatus(snapshot: SystemStatusSnapshot): void;
  emitAgvs(snapshot: AgvsSnapshot): void;
  emitMessages(snapshot: MessagesSnapshot): void;
  emitMap(snapshot: MapSnapshot): void;
  emitEquipment(snapshot: EquipmentStatusSnapshot): void;
  emitTransports(snapshot: TransportsSnapshot): void;
  emitConsumables(snapshot: ConsumablesSnapshot): void;
  /** 取りやめを頼まれた搬送（頼まれた順）。 */
  readonly canceledTransports: readonly number[];
  /** 取りやめの結果を決める（既定は canceled）。holdResponses で止めておける。 */
  respondToCancel(result: TransportCancelResult): void;
}

/**
 * 偽物のつながりを作る。
 * @param initial つないだときに、すぐ送るもの（Storybook で、つながった形を見るため）。
 */
export function createFakeLink(initial?: {
  readonly connection: ConnectionState;
  readonly status?: SystemStatusSnapshot;
  readonly agvs?: AgvsSnapshot;
  readonly messages?: MessagesSnapshot;
  readonly map?: MapSnapshot;
  readonly equipment?: EquipmentStatusSnapshot;
  readonly transports?: TransportsSnapshot;
  readonly consumables?: ConsumablesSnapshot;
}): FakeLink {
  let handlers: ServerHandlers | null = null;
  let result: CommandResult = 'ok';
  let holding = false;
  let held: (() => void)[] = [];
  const sent: Command[] = [];
  const agvCommands: { id: AgvId; command: AgvCommand }[] = [];
  let agvResult: AgvCommandResult = 'accepted';
  const consumableCommands: ConsumableCommand[] = [];
  const canceledTransports: number[] = [];
  let cancelResult: TransportCancelResult = 'canceled';
  let consumableResult: ConsumableCommandResult | null = null;
  const accepted = { stockerSetting: 'saved', ship: 'requested', correct: 'corrected', designate: 'designated', cancelDesignation: 'canceled' } as const;

  const send = (command: Command): Promise<CommandResult> => {
    sent.push(command);
    const answer = result;
    if (!holding) {
      return Promise.resolve(answer);
    }
    return new Promise((resolve) => held.push(() => resolve(answer)));
  };

  const operate = (id: AgvId, command: AgvCommand): Promise<AgvCommandResult> => {
    agvCommands.push({ id, command });
    const answer = agvResult;
    if (!holding) {
      return Promise.resolve(answer);
    }
    return new Promise((resolve) => held.push(() => resolve(answer)));
  };

  const operateConsumables = (command: ConsumableCommand): Promise<ConsumableCommandResult> => {
    consumableCommands.push(command);
    const answer = consumableResult ?? accepted[command.kind];
    if (!holding) {
      return Promise.resolve(answer);
    }
    return new Promise((resolve) => held.push(() => resolve(answer)));
  };

  return {
    sent,
    agvCommands,
    consumableCommands,
    canceledTransports,
    respondToCancel(next) {
      cancelResult = next;
    },
    cancelTransport(number) {
      canceledTransports.push(number);
      const answer = cancelResult;
      if (!holding) {
        return Promise.resolve(answer);
      }
      return new Promise((resolve) => held.push(() => resolve(answer)));
    },
    respondToConsumables(next) {
      consumableResult = next;
    },
    operateConsumables,
    respondToAgv(next) {
      agvResult = next;
    },
    operateAgv: operate,
    get connected() {
      return handlers !== null;
    },
    connect(h) {
      handlers = h;
      if (initial) {
        h.onConnection(initial.connection);
        if (initial.status) {
          h.onSystemStatus(initial.status);
        }
        if (initial.agvs) {
          h.onAgvs(initial.agvs);
        }
        if (initial.messages) {
          h.onMessages(initial.messages);
        }
        if (initial.map) {
          h.onMap(initial.map);
        }
        if (initial.equipment) {
          h.onEquipment(initial.equipment);
        }
        if (initial.transports) {
          h.onTransports(initial.transports);
        }
        if (initial.consumables) {
          h.onConsumables(initial.consumables);
        }
      }
      return () => {
        if (handlers === h) {
          handlers = null;
        }
      };
    },
    startSystem: () => send('start'),
    stopSystem: () => send('stop'),
    respondWith(next) {
      result = next;
    },
    holdResponses() {
      holding = true;
    },
    release() {
      holding = false;
      const pending = held;
      held = [];
      pending.forEach((resolve) => resolve());
    },
    emitConnection(state) {
      handlers?.onConnection(state);
    },
    emitSystemStatus(snapshot) {
      handlers?.onSystemStatus(snapshot);
    },
    emitAgvs(snapshot) {
      handlers?.onAgvs(snapshot);
    },
    emitMessages(snapshot) {
      handlers?.onMessages(snapshot);
    },
    emitMap(snapshot) {
      handlers?.onMap(snapshot);
    },
    emitEquipment(snapshot) {
      handlers?.onEquipment(snapshot);
    },
    emitTransports(snapshot) {
      handlers?.onTransports(snapshot);
    },
    emitConsumables(snapshot) {
      handlers?.onConsumables(snapshot);
    },
  };
}
