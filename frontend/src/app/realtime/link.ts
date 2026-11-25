import type { CommandResult } from '../../shared/api/postCommand';
import type { AgvId } from '../../features/fleet/agv';
import type { AgvCommand, AgvCommandResult } from '../../features/fleet/api/agvCommands';
import type { AgvsSnapshot } from '../../features/fleet/api/agvSnapshots';
import type { MapSnapshot } from '../../features/routing/api/mapSnapshot';
import type { EquipmentStatusSnapshot } from '../../features/tool/api/equipmentStatus';
import type { TransportsSnapshot } from '../../features/dispatch/api/transportsSnapshot';
import type { ConsumablesSnapshot } from '../../features/tool/api/consumablesSnapshot';
import type { TransportCancelResult } from '../../features/dispatch/api/transportCommands';
import type { ConsumableCommand, ConsumableCommandResult } from '../../features/tool/api/consumableCommands';
import type { MessagesSnapshot } from '../../features/alarm/api/messages';
import type { SystemStatusSnapshot } from '../../features/system/systemStatus';

/**
 * サーバーとのつながりの様子。
 * - connecting：初めてつなごうとしている（つながるまで、間を空けて試し続ける）。
 * - connected：つながっている。
 * - reconnecting：切れたので、つなぎ直している。
 */
export type ConnectionState = 'connecting' | 'connected' | 'reconnecting';

/** サーバーから届くものを受け取る。 */
export interface ServerHandlers {
  readonly onConnection: (state: ConnectionState) => void;
  readonly onSystemStatus: (snapshot: SystemStatusSnapshot) => void;
  readonly onAgvs: (snapshot: AgvsSnapshot) => void;
  readonly onMessages: (snapshot: MessagesSnapshot) => void;
  readonly onMap: (snapshot: MapSnapshot) => void;
  readonly onEquipment: (snapshot: EquipmentStatusSnapshot) => void;
  readonly onTransports: (snapshot: TransportsSnapshot) => void;
  readonly onConsumables: (snapshot: ConsumablesSnapshot) => void;
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
  /** 消耗品の操作を送る（ストッカーの設定、出庫、要確認を直す。応答がなければ、あきらめて failed。やり直さない）。 */
  operateConsumables(command: ConsumableCommand): Promise<ConsumableCommandResult>;
  /** 搬送を取りやめる（応答がなければ、あきらめて failed。やり直さない）。 */
  cancelTransport(number: number): Promise<TransportCancelResult>;
}

/** 操作を送って、応答を待つ長さ。 */
export const COMMAND_TIMEOUT_MS = 10_000;

/** つなぎ直すまでの間（それまでに試した回数 → ミリ秒）。すぐ、2秒、5秒、そのあとは 10 秒ごとに、ずっと続ける。 */
export function retryDelayMs(previousRetryCount: number): number {
  const delays = [0, 2000, 5000];
  return delays[previousRetryCount] ?? 10_000;
}
