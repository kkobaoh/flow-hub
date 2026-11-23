import type { AgvDetail } from '../app/AgvCard';
import type { AgvOperation, ControlStatus } from '../app/controlStatus';
import { agvId, type Activity, type AgvPosition } from '../map/agv';
import { heading, panelId, type MapLayout } from '../map/model';
import { parseVersion, type Versioned } from './versioned';

/** サーバーの AGV の状態（サーバーの AgvSnapshot.State と同じ言葉）。 */
export type AgvServerState = 'unregistered' | 'resettingPosition' | 'idle' | 'moving' | 'emergencyStopped' | 'error';

/** サーバーから届く、1台の AGV の様子（届いた形のまま）。 */
export interface AgvWire {
  readonly id: string;
  /** いるパネル。位置がわからなければ null。 */
  readonly panel: string | null;
  /** 向き（0〜359）。位置がわからなければ null。 */
  readonly heading: number | null;
  readonly state: AgvServerState;
  readonly control: ControlStatus;
  readonly carrierMoving: boolean;
  /** 最後に届いた充電残量（%）。まだ届いていなければ null。 */
  readonly batteryPercent: number | null;
}

/** サーバーから届く、すべての AGV の様子。変わるたびに番号が進む。 */
export interface AgvsSnapshot extends Versioned {
  readonly agvs: readonly AgvWire[];
}

const STATES: ReadonlySet<string> = new Set<AgvServerState>(['unregistered', 'resettingPosition', 'idle', 'moving', 'emergencyStopped', 'error']);
const CONTROLS: ReadonlySet<string> = new Set<ControlStatus>(['auto', 'assignmentPaused', 'manual', 'locked', 'detached']);

/** サーバーから届いた値を検証して、AGV の様子にする。形が正しくなければ例外にする。 */
export function parseAgvs(value: unknown): AgvsSnapshot {
  if (typeof value !== 'object' || value === null) {
    throw new TypeError('AGV の様子の形が正しくありません。');
  }
  const { version, agvs } = value as Record<string, unknown>;
  if (!Array.isArray(agvs)) {
    throw new TypeError('AGV の様子に、AGV の並びがありません。');
  }
  return { version: parseVersion(version, 'AGV の様子'), agvs: agvs.map(parseAgv) };
}

function parseAgv(value: unknown): AgvWire {
  if (typeof value !== 'object' || value === null) {
    throw new TypeError('AGV の形が正しくありません。');
  }
  const v = value as Record<string, unknown>;
  if (typeof v.id !== 'string') {
    throw new TypeError('AGV の ID がありません。');
  }
  const id = agvId(v.id);
  if (typeof v.state !== 'string' || !STATES.has(v.state)) {
    throw new TypeError(`${id} の状態がわかりません（${String(v.state)}）。`);
  }
  if (typeof v.control !== 'string' || !CONTROLS.has(v.control)) {
    throw new TypeError(`${id} のコントロールステータスがわかりません（${String(v.control)}）。`);
  }
  if (typeof v.carrierMoving !== 'boolean') {
    throw new TypeError(`${id} のキャリアの様子がありません。`);
  }
  const located = v.panel !== null && v.panel !== undefined;
  if (located !== (v.heading !== null && v.heading !== undefined)) {
    throw new TypeError(`${id} のパネルと向きは、両方あるか両方ないかのどちらかです。`);
  }
  if (located && (typeof v.panel !== 'string' || typeof v.heading !== 'number')) {
    throw new TypeError(`${id} の位置の形が正しくありません。`);
  }
  const battery = v.batteryPercent ?? null;
  if (battery !== null && (typeof battery !== 'number' || !Number.isInteger(battery) || battery < 0 || battery > 100)) {
    throw new TypeError(`${id} の充電残量が正しくありません（${String(battery)}）。`);
  }
  return {
    id,
    panel: located ? panelId(v.panel as string) : null,
    heading: located ? heading(v.heading as number) : null,
    state: v.state as AgvServerState,
    control: v.control as ControlStatus,
    carrierMoving: v.carrierMoving,
    batteryPercent: battery,
  };
}

/** サーバーの状態を、画面の動き（標示灯）と状態の言葉にする（docs/screens.md「画面とサーバーの通信」）。 */
function operationOf(state: AgvServerState): { readonly activity: Activity; readonly operation: AgvOperation } {
  switch (state) {
    case 'idle':
      return { activity: 'idle', operation: 'idle' };
    case 'moving':
      return { activity: 'moving', operation: 'traveling' };
    case 'emergencyStopped':
      return { activity: 'idle', operation: 'emergencyStopped' };
    case 'error':
      return { activity: 'idle', operation: 'fault' };
    case 'resettingPosition':
      return { activity: 'idle', operation: 'awaitingPositionReset' };
    case 'unregistered':
      return { activity: 'idle', operation: 'positionUnknown' };
    default:
      throw new Error(`知らない AGV の状態です: ${JSON.stringify(state satisfies never)}`);
  }
}

function positionOf(agv: AgvWire, layout: MapLayout): AgvPosition {
  if (agv.panel === null || agv.heading === null) {
    return { kind: 'unknown' };
  }
  const panel = panelId(agv.panel);
  const h = heading(agv.heading);
  return layout.panels.some((p) => p.id === panel) ? { kind: 'onMap', panel, heading: h } : { kind: 'offMap', panel, heading: h };
}

/**
 * サーバーから届いた AGV の様子を、カードと一覧とマップで使う形にする。
 * - 印の名前は、ID そのまま。
 * - 描いているマップにないパネルにいれば、マップの外（offMap）にする。
 * - 充電しているかは、AGV から知らせがないので、わからないにする。
 * - 割り当て済みの搬送は、まだサーバーから届かないので、なしにする。
 */
export function toAgvDetails(agvs: readonly AgvWire[], layout: MapLayout): AgvDetail[] {
  return agvs.map((agv) => {
    const { activity, operation } = operationOf(agv.state);
    return {
      id: agvId(agv.id),
      label: agv.id,
      position: positionOf(agv, layout),
      status: { activity, charging: 'unknown', carrierMoving: agv.carrierMoving },
      control: agv.control,
      operation,
      transport: null,
      batteryPercent: agv.batteryPercent,
    };
  });
}
