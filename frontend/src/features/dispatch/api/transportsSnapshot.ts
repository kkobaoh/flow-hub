import { agvId } from '../../fleet/agv';
import { STAGE_LABELS, type TransportStageWire } from '../../fleet/api/agvSnapshots';
import { parseVersion, type Versioned } from '../../../shared/realtime/versioned';
import type { Transport, TransportStatus } from '../transports';

/** 消耗品の搬送の目的（頼んだ側から聞いたもの）。 */
export type TransportPurpose = 'delivery' | 'recovery' | 'deliveryFromAgv' | 'returnFromAgv';

/** 搬送の段階。waiting は AGV の割り当て待ち、ほかは AGV の様子の搬送と同じ。 */
export type TransportListStage = 'waiting' | TransportStageWire;

/** サーバーから届く、1つの搬送。 */
export interface TransportData {
  readonly id: number;
  /** 搬送元の名前（受け渡しの相手か、ものを積んだまま止まっていた AGV）。 */
  readonly source: string;
  /** 搬送先の名前。 */
  readonly destination: string;
  readonly neededBy: Date;
  /** 目的。頼んだ側から聞けなければ null。 */
  readonly purpose: TransportPurpose | null;
  /** 運ぶものの、品種ごとの数（品種がわからないものは null）。 */
  readonly cargo: readonly { readonly variety: string | null; readonly count: number }[];
  readonly stage: TransportListStage;
  /** 割り当てた AGV（割り当て待ちなら null）。 */
  readonly agv: string | null;
  readonly paused: boolean;
}

/** サーバーから届く搬送の一覧。変わるたびに番号が進む。 */
export interface TransportsSnapshot extends Versioned {
  readonly transports: readonly TransportData[];
}

const PURPOSES: readonly TransportPurpose[] = ['delivery', 'recovery', 'deliveryFromAgv', 'returnFromAgv'];
const STAGES: ReadonlySet<string> = new Set(['waiting', ...Object.keys(STAGE_LABELS)]);

const isRecord = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null;

function text(value: unknown, what: string): string {
  if (typeof value !== 'string' || value.trim() === '') {
    throw new TypeError(`${what}がありません。`);
  }
  return value;
}

function positive(value: unknown, what: string): number {
  if (typeof value !== 'number' || !Number.isInteger(value) || value < 1) {
    throw new TypeError(`${what}が正しくありません（${String(value)}）。`);
  }
  return value;
}

function parseTransport(value: unknown): TransportData {
  if (!isRecord(value)) {
    throw new TypeError('搬送の形が正しくありません。');
  }
  const id = positive(value.id, '搬送の番号');
  const what = `搬送 #${id} の`;
  const neededBy = typeof value.neededBy === 'string' ? new Date(value.neededBy) : new Date(Number.NaN);
  if (Number.isNaN(neededBy.getTime())) {
    throw new TypeError(`${what}必要時刻が正しくありません（${String(value.neededBy)}）。`);
  }
  if (value.purpose !== null && !(PURPOSES as readonly unknown[]).includes(value.purpose)) {
    throw new TypeError(`${what}目的がわかりません（${String(value.purpose)}）。`);
  }
  if (typeof value.stage !== 'string' || !STAGES.has(value.stage)) {
    throw new TypeError(`${what}段階がわかりません（${String(value.stage)}）。`);
  }
  const stage = value.stage as TransportListStage;
  const agv = value.agv === null ? null : text(value.agv, `${what}AGV`);
  if ((stage === 'waiting') !== (agv === null)) {
    throw new TypeError(`${what}AGV と段階が合いません。`);
  }
  if (!Array.isArray(value.cargo) || typeof value.paused !== 'boolean') {
    throw new TypeError(`${what}形が正しくありません。`);
  }
  return {
    id,
    source: text(value.source, `${what}搬送元`),
    destination: text(value.destination, `${what}搬送先`),
    neededBy,
    purpose: value.purpose as TransportPurpose | null,
    cargo: value.cargo.map((c: unknown) => {
      if (!isRecord(c)) {
        throw new TypeError(`${what}運ぶものの形が正しくありません。`);
      }
      return { variety: c.variety === null ? null : text(c.variety, `${what}品種`), count: positive(c.count, `${what}運ぶものの数`) };
    }),
    stage,
    agv,
    paused: value.paused,
  };
}

/** サーバーから届いた値を検証して、搬送の一覧にする。形が正しくなければ例外にする。 */
export function parseTransports(value: unknown): TransportsSnapshot {
  if (!isRecord(value) || !Array.isArray(value.transports)) {
    throw new TypeError('搬送の一覧の形が正しくありません。');
  }
  return { version: parseVersion(value.version, '搬送の一覧'), transports: value.transports.map(parseTransport) };
}

const KIND_LABELS: Record<TransportPurpose, string> = {
  delivery: '届ける',
  recovery: '回収する',
  deliveryFromAgv: '届ける（AGV から）',
  returnFromAgv: '返す（AGV から）',
};

function statusOf(stage: TransportListStage): TransportStatus {
  switch (stage) {
    case 'waiting':
      return { stage: 'queued' };
    case 'interrupted':
      return { stage: 'suspended' };
    default:
      return { stage: 'running' };
  }
}

/** 運ぶものの文。目的がわからない（頼んだ側から聞けなかった）ものは、数だけ。 */
function cargoText(t: TransportData): string {
  if (t.purpose === null) {
    return `${t.cargo.reduce((sum, c) => sum + c.count, 0)}個`;
  }
  return t.cargo.map((c) => `${c.variety ?? '不明な消耗品'} ${c.count}個`).join('、');
}

/** サーバーから届いた搬送を、搬送の一覧の画面に出す形にする。 */
export function toTransports(transports: readonly TransportData[]): Transport[] {
  return transports.map((t) => ({
    number: t.id,
    status: statusOf(t.stage),
    kind: t.purpose === null ? '－' : KIND_LABELS[t.purpose],
    from: t.source,
    to: t.destination,
    cargo: cargoText(t),
    neededBy: t.neededBy,
    agv: t.agv === null ? null : agvId(t.agv),
    step: t.stage === 'waiting' ? '割り当て待ち' : STAGE_LABELS[t.stage],
    stepNote: t.paused ? '一時停止中' : '',
  }));
}
