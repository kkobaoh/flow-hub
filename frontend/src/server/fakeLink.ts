import type { CommandResult, ConnectionState, ServerHandlers, ServerLink } from './link';
import type { AgvId } from '../map/agv';
import type { AgvCommand, AgvCommandResult } from './agvCommands';
import type { AgvsSnapshot } from './agvs';
import type { SystemStatusSnapshot } from './systemStatus';

type Command = 'start' | 'stop';

/** テストと Storybook で使う、偽物のつながり。届くものを手で送り、送られた操作を覚える。 */
export interface FakeLink extends ServerLink {
  /** 送られた操作。 */
  readonly sent: readonly Command[];
  /** AGV へ送られた操作。 */
  readonly agvCommands: readonly { readonly id: AgvId; readonly command: AgvCommand }[];
  /** AGV への操作の結果を決める（既定は accepted）。holdResponses で止めておける。 */
  respondToAgv(result: AgvCommandResult): void;
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
}

/**
 * 偽物のつながりを作る。
 * @param initial つないだときに、すぐ送るもの（Storybook で、つながった形を見るため）。
 */
export function createFakeLink(initial?: {
  readonly connection: ConnectionState;
  readonly status?: SystemStatusSnapshot;
  readonly agvs?: AgvsSnapshot;
}): FakeLink {
  let handlers: ServerHandlers | null = null;
  let result: CommandResult = 'ok';
  let holding = false;
  let held: (() => void)[] = [];
  const sent: Command[] = [];
  const agvCommands: { id: AgvId; command: AgvCommand }[] = [];
  let agvResult: AgvCommandResult = 'accepted';

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

  return {
    sent,
    agvCommands,
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
  };
}
