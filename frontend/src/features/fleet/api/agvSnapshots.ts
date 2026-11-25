import type { AgvDetail, AgvTransport } from '../AgvCard';
import type { AgvOperation, ControlStatus } from '../controlStatus';
import { agvId, type Activity, type AgvPosition } from '../agv';
import { heading, panelId, type MapLayout, type PanelId } from '../../routing/model';
import { parseVersion, type Versioned } from '../../../shared/realtime/versioned';

/** サーバーの AGV の状態（サーバーの AgvSnapshot.State と同じ言葉）。 */
export type AgvServerState = 'unregistered' | 'resettingPosition' | 'idle' | 'moving' | 'emergencyStopped' | 'error';

/** 割り当て済みの搬送の段階（サーバーの AgvTransportSnapshot.Stage と同じ言葉）。 */
export type TransportStageWire =
  | 'movingToSource'
  | 'loweringAtSource'
  | 'transferringAtSource'
  | 'raisingAtSource'
  | 'movingToDestination'
  | 'loweringAtDestination'
  | 'transferringAtDestination'
  | 'raisingAtDestination'
  | 'interrupted';

/** 段階を、画面に出す言葉にする（搬送の一覧の見本と同じ言い方）。 */
export const STAGE_LABELS: Record<TransportStageWire, string> = {
  movingToSource: '搬送元へ移動',
  loweringAtSource: '搬送元でキャリアを下げている',
  transferringAtSource: '搬送元で受け渡し中',
  raisingAtSource: '搬送元でキャリアを上げている',
  movingToDestination: '搬送先へ移動',
  loweringAtDestination: '搬送先でキャリアを下げている',
  transferringAtDestination: '搬送先で受け渡し中',
  raisingAtDestination: '搬送先でキャリアを上げている',
  interrupted: '中断',
};

/** サーバーから届く、割り当て済みの搬送。 */
export interface TransportWire {
  readonly id: number;
  /** 搬送元の名前（受け渡しの相手か、ものを積んだまま止まっていた AGV）。 */
  readonly from: string;
  /** 搬送先の名前。 */
  readonly to: string;
  readonly stage: TransportStageWire;
  readonly paused: boolean;
}

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
  /** 予約しているパネル（いるパネルも含む）。 */
  readonly reservedPanels: readonly string[];
  /** 走る予定の経路（今のパネルから行き先まで）。走る予定がなければ空。 */
  readonly route: readonly string[];
  /** 割り当て済みの搬送。なければ null。 */
  readonly transport: TransportWire | null;
}

/** サーバーから届く、すべての AGV の様子。変わるたびに番号が進む。 */
export interface AgvsSnapshot extends Versioned {
  readonly agvs: readonly AgvWire[];
}

const STATES: ReadonlySet<string> = new Set<AgvServerState>(['unregistered', 'resettingPosition', 'idle', 'moving', 'emergencyStopped', 'error']);
const STAGES: ReadonlySet<string> = new Set(Object.keys(STAGE_LABELS));
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
    reservedPanels: parsePanels(v.reservedPanels, `${id} の予約しているパネル`),
    route: parsePanels(v.route, `${id} の経路`),
    transport: parseTransport(v.transport ?? null, id),
    panel: located ? panelId(v.panel as string) : null,
    heading: located ? heading(v.heading as number) : null,
    state: v.state as AgvServerState,
    control: v.control as ControlStatus,
    carrierMoving: v.carrierMoving,
    batteryPercent: battery,
  };
}

function parsePanels(value: unknown, what: string): string[] {
  if (!Array.isArray(value)) {
    throw new TypeError(`${what}が並びではありません。`);
  }
  return value.map((p: unknown) => {
    if (typeof p !== 'string') {
      throw new TypeError(`${what}に、パネルでないものがあります。`);
    }
    return panelId(p);
  });
}

function parseTransport(value: unknown, id: string): TransportWire | null {
  if (value === null) {
    return null;
  }
  if (typeof value !== 'object') {
    throw new TypeError(`${id} の搬送の形が正しくありません。`);
  }
  const t = value as Record<string, unknown>;
  if (typeof t.id !== 'number' || !Number.isInteger(t.id) || t.id < 1) {
    throw new TypeError(`${id} の搬送の番号が正しくありません（${String(t.id)}）。`);
  }
  if (typeof t.from !== 'string' || typeof t.to !== 'string') {
    throw new TypeError(`${id} の搬送の、搬送元か搬送先がありません。`);
  }
  if (typeof t.stage !== 'string' || !STAGES.has(t.stage)) {
    throw new TypeError(`${id} の搬送の段階がわかりません（${String(t.stage)}）。`);
  }
  if (typeof t.paused !== 'boolean') {
    throw new TypeError(`${id} の搬送が、一時停止しているかがわかりません。`);
  }
  return { id: t.id, from: t.from, to: t.to, stage: t.stage as TransportStageWire, paused: t.paused };
}

function transportOf(t: TransportWire | null): AgvTransport | null {
  if (t === null) {
    return null;
  }
  const step = STAGE_LABELS[t.stage];
  return { label: `#${t.id} ${t.from} から ${t.to} へ`, step: t.paused ? `${step}（一時停止中）` : step };
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
 * - 割り当て済みの搬送は、番号と搬送元から搬送先（「#12 ST-1 から DEV-1 へ」）と、今の段階にする。
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
      transport: transportOf(agv.transport),
      batteryPercent: agv.batteryPercent,
    };
  });
}

/** マップに重ねて描く、予約しているパネルと走る予定の経路。 */
export interface MapOverlays {
  readonly reservedPanels: readonly PanelId[];
  readonly routes: readonly (readonly PanelId[])[];
}

/**
 * すべての AGV の予約しているパネル（いるパネルも含む。重ねずに集める）と、AGV ごとの走る予定の経路を、マップに描く形にする。
 * 描いているマップにないパネルは予約から外し、そのパネルを通る経路は描かない（マップが見本の配置のあいだ、食い違いうるため）。
 */
export function mapOverlays(agvs: readonly AgvWire[], layout: MapLayout): MapOverlays {
  const onMap = new Set<string>(layout.panels.map((p) => p.id));
  const reserved = new Set<PanelId>();
  for (const agv of agvs) {
    for (const p of agv.reservedPanels) {
      if (onMap.has(p)) {
        reserved.add(panelId(p));
      }
    }
  }
  const routes = agvs.filter((a) => a.route.length >= 2 && a.route.every((p) => onMap.has(p))).map((a) => a.route.map(panelId));
  return { reservedPanels: [...reserved], routes };
}
