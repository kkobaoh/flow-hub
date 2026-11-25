import { postOperation } from '../../../shared/api/postCommand';
import { parseVersion, type Versioned } from '../../../shared/realtime/versioned';
import { agvId } from '../agv';
import type { AgvEntry, AgvType, ChargeDefaults, Registration } from '../agvs';
import type { ControlStatus } from '../controlStatus';

/** サーバーから届く、AGV と AGV の種類の登録。変わるたびに番号が進む。 */
export interface FleetSnapshot extends Versioned {
  readonly types: readonly AgvType[];
  readonly entries: readonly AgvEntry[];
  /** 充電の残量の既定値（種類を加えるときに省いたら、これで埋める）。 */
  readonly defaults: ChargeDefaults;
  /** AGV に付けられるキャリアの種類（加工の種類にあるもの）。 */
  readonly carriers: readonly string[];
}

const CONTROLS: readonly ControlStatus[] = ['auto', 'assignmentPaused', 'manual', 'locked', 'detached'];
const REGISTRATIONS: readonly Registration[] = ['both', 'trafficOnly', 'dispatchOnly'];

const isRecord = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null;

function text(value: unknown, what: string): string {
  if (typeof value !== 'string' || value.trim() === '') {
    throw new TypeError(`${what}がありません。`);
  }
  return value;
}

const optionalText = (value: unknown, what: string): string | null => (value === null ? null : text(value, what));

function number(value: unknown, what: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    throw new TypeError(`${what}が正しくありません（${String(value)}）。`);
  }
  return value;
}

function records(value: unknown, what: string): readonly Record<string, unknown>[] {
  if (!Array.isArray(value) || !value.every(isRecord)) {
    throw new TypeError(`${what}の並びが正しくありません。`);
  }
  return value;
}

function oneOf<T extends string>(value: unknown, choices: readonly T[], what: string): T {
  if (typeof value !== 'string' || !(choices as readonly string[]).includes(value)) {
    throw new TypeError(`${what}が正しくありません（${String(value)}）。`);
  }
  return value as T;
}

/** サーバーから届いた値を検証して、AGV と AGV の種類の登録にする。形が正しくなければ例外にする。 */
export function parseFleet(value: unknown): FleetSnapshot {
  if (!isRecord(value) || !isRecord(value.defaults) || !Array.isArray(value.carriers)) {
    throw new TypeError('AGV と AGV の種類の登録の形が正しくありません。');
  }
  return {
    version: parseVersion(value.version, 'AGV と AGV の種類の登録'),
    types: records(value.types, 'AGV の種類').map((t) => {
      const id = text(t.id, '種類の ID');
      if (typeof t.special !== 'boolean') {
        throw new TypeError(`種類 ${id} の、特殊消耗品受け渡しパネルを走れるかが正しくありません。`);
      }
      return {
        id,
        name: text(t.name, `種類 ${id} の名前`),
        forward: number(t.forward, `種類 ${id} の前進の速さ`),
        backward: number(t.backward, `種類 ${id} の後退の速さ`),
        rotation: number(t.rotation, `種類 ${id} の回転の速さ`),
        chargeAt: number(t.chargeAt, `種類 ${id} の充電に向かう残量`),
        resumeAt: number(t.resumeAt, `種類 ${id} の割り当てに戻す残量`),
        special: t.special,
      };
    }),
    entries: records(value.agvs, 'AGV').map((a) => {
      const id = text(a.id, 'AGV の ID');
      return {
        id: agvId(id),
        type: optionalText(a.type, `${id} の種類`),
        carrier: optionalText(a.carrier, `${id} のキャリアの種類`),
        control: a.control === null ? null : oneOf(a.control, CONTROLS, `${id} のコントロールステータス`),
        registration: oneOf(a.registration, REGISTRATIONS, `${id} の登録`),
      };
    }),
    defaults: { chargeAt: number(value.defaults.chargeAt, '充電に向かう残量の既定値'), resumeAt: number(value.defaults.resumeAt, '割り当てに戻す残量の既定値') },
    carriers: value.carriers.map((c: unknown) => text(c, 'キャリアの種類')),
  };
}

/** 画面から送る、AGV の種類の操作。 */
export type AgvTypeCommand = { readonly kind: 'add' | 'edit'; readonly type: AgvType } | { readonly kind: 'delete'; readonly id: string };

/**
 * 操作の結果（サーバーの FleetEndpoints と同じ言葉）。
 * - saved・deleted：受け付けた。idTaken・nameTaken・notFound・inUse：断った（何も変えていない）。
 * - failed：片方の DB に書けず元に戻した、応答がない、または思わない応答（やり直さない）。
 */
export type AgvTypeCommandResult = 'saved' | 'deleted' | 'idTaken' | 'nameTaken' | 'notFound' | 'inUse' | 'failed';

const SERVER_RESULTS: ReadonlySet<Exclude<AgvTypeCommandResult, never>> = new Set(['saved', 'deleted', 'idTaken', 'nameTaken', 'notFound', 'inUse', 'failed'] as const);

export function agvTypeCommandRequest(command: AgvTypeCommand): { readonly path: string; readonly body: Record<string, unknown> } {
  switch (command.kind) {
    case 'add':
      return { path: '/agv-types', body: { ...command.type } };
    case 'edit':
      return { path: `/agv-types/${encodeURIComponent(command.type.id)}/edit`, body: { ...command.type } };
    case 'delete':
      return { path: `/agv-types/${encodeURIComponent(command.id)}/delete`, body: {} };
    default:
      throw new Error(`知らない操作です: ${JSON.stringify(command satisfies never)}`);
  }
}

/** 操作を送る。timeoutMs を過ぎても応答がなければ、あきらめて failed にする（やり直さない）。 */
export async function sendAgvTypeCommand(baseUrl: string, command: AgvTypeCommand, timeoutMs: number): Promise<AgvTypeCommandResult> {
  const { path, body } = agvTypeCommandRequest(command);
  const result = await postOperation(baseUrl, path, body, timeoutMs, SERVER_RESULTS);
  return result === 'refused' ? 'failed' : result;
}

/** 画面に出す、操作の結果の知らせ（受け付けたときは null）。 */
export function agvTypeNotice(result: AgvTypeCommandResult): string | null {
  switch (result) {
    case 'saved':
    case 'deleted':
      return null;
    case 'idTaken':
      return 'その ID の種類は、もう登録してあります。';
    case 'nameTaken':
      return 'その名前の種類は、もう登録してあります。';
    case 'notFound':
      return 'その種類は、もうありません。表示を確かめてください。';
    case 'inUse':
      return 'その種類を使う AGV があるので、消せませんでした。';
    case 'failed':
      return '保存できませんでした。サーバーから応答がないか、走行と配車の片方に書けませんでした。表示を確かめてください。';
    default:
      throw new Error(`知らない結果です: ${JSON.stringify(result satisfies never)}`);
  }
}
