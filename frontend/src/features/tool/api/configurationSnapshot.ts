import { postOperation } from '../../../shared/api/postCommand';
import { parseVersion, type Versioned } from '../../../shared/realtime/versioned';
import type { ProcessKind, Variety } from '../config';

/** サーバーから届く、消耗品の構成（加工の種類と品種）。変わるたびに番号が進む。 */
export interface ConfigurationSnapshot extends Versioned {
  /** 加工の種類（ID の順）。 */
  readonly processKinds: readonly ProcessKind[];
  /** 品種（ID の順）。 */
  readonly varieties: readonly Variety[];
}

const isRecord = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null;

function text(value: unknown, what: string): string {
  if (typeof value !== 'string' || value.trim() === '') {
    throw new TypeError(`${what}がありません。`);
  }
  return value;
}

function records(value: unknown, what: string): readonly Record<string, unknown>[] {
  if (!Array.isArray(value) || !value.every(isRecord)) {
    throw new TypeError(`${what}の並びが正しくありません。`);
  }
  return value;
}

function texts(value: unknown, what: string): readonly string[] {
  if (!Array.isArray(value)) {
    throw new TypeError(`${what}の並びが正しくありません。`);
  }
  return value.map((v: unknown) => text(v, what));
}

/** サーバーから届いた値を検証して、消耗品の構成にする。形が正しくなければ例外にする。 */
export function parseConfiguration(value: unknown): ConfigurationSnapshot {
  if (!isRecord(value)) {
    throw new TypeError('消耗品の構成の形が正しくありません。');
  }
  return {
    version: parseVersion(value.version, '消耗品の構成'),
    processKinds: records(value.processTypes, '加工の種類').map((p) => {
      const name = text(p.id, '加工の種類の名前');
      const capacity = p.capacity;
      if (typeof capacity !== 'number' || !Number.isInteger(capacity) || capacity < 1) {
        throw new TypeError(`加工の種類 ${name} のキャリアに入る個数が正しくありません（${String(capacity)}）。`);
      }
      return { name, carrier: text(p.carrier, `加工の種類 ${name} のキャリアの種類`), capacity };
    }),
    varieties: records(value.varieties, '品種').map((v) => {
      const name = text(v.id, '品種の名前');
      if (typeof v.inUse !== 'boolean') {
        throw new TypeError(`品種 ${name} の、使われているかが正しくありません。`);
      }
      const expected = texts(v.expected, `品種 ${name} の入庫の予定`);
      const stocked = texts(v.stocked, `品種 ${name} の在庫にある ID`);
      // 画面では、入庫の予定と在庫にある ID をまとめて「対応させた ID」として出す（在庫にある ID は外せない）
      return { name, processKind: text(v.processType, `品種 ${name} の加工の種類`), ids: [...expected, ...stocked], stockedIds: stocked, inUse: v.inUse };
    }),
  };
}

/** 画面から送る、消耗品の構成の操作。 */
export type ConfigurationCommand =
  | { readonly kind: 'addProcessKind' | 'editProcessKind'; readonly processKind: ProcessKind }
  | { readonly kind: 'deleteProcessKind'; readonly name: string }
  | { readonly kind: 'addVariety' | 'editVariety'; readonly name: string; readonly processKind: string }
  | { readonly kind: 'deleteVariety'; readonly name: string }
  | { readonly kind: 'addId'; readonly variety: string; readonly id: string }
  | { readonly kind: 'removeId'; readonly id: string };

/**
 * 操作の結果（サーバーの ConfigurationEndpoints と同じ言葉）。
 * - saved：受け付けた。alreadyRegistered・notFound・inUse：断った（何も変えていない）。
 * - failed：応答がない、または思わない応答（やり直さない）。
 */
export type ConfigurationResult = 'saved' | 'alreadyRegistered' | 'notFound' | 'inUse' | 'failed';

const SERVER_RESULTS: ReadonlySet<ConfigurationResult> = new Set(['saved', 'alreadyRegistered', 'notFound', 'inUse'] as const);

const segment = encodeURIComponent;

export function configurationRequest(command: ConfigurationCommand): { readonly path: string; readonly body: Record<string, unknown> } {
  switch (command.kind) {
    case 'addProcessKind':
      return { path: '/configuration/process-types', body: { id: command.processKind.name, carrier: command.processKind.carrier, capacity: command.processKind.capacity } };
    case 'editProcessKind':
      return {
        path: `/configuration/process-types/${segment(command.processKind.name)}/edit`,
        body: { carrier: command.processKind.carrier, capacity: command.processKind.capacity },
      };
    case 'deleteProcessKind':
      return { path: `/configuration/process-types/${segment(command.name)}/delete`, body: {} };
    case 'addVariety':
      return { path: '/configuration/varieties', body: { id: command.name, processType: command.processKind } };
    case 'editVariety':
      return { path: `/configuration/varieties/${segment(command.name)}/edit`, body: { processType: command.processKind } };
    case 'deleteVariety':
      return { path: `/configuration/varieties/${segment(command.name)}/delete`, body: {} };
    case 'addId':
      return { path: `/configuration/varieties/${segment(command.variety)}/expected-arrivals`, body: { consumable: command.id } };
    case 'removeId':
      return { path: `/configuration/expected-arrivals/${segment(command.id)}/delete`, body: {} };
    default:
      throw new Error(`知らない操作です: ${JSON.stringify(command satisfies never)}`);
  }
}

/** 操作を送る。timeoutMs を過ぎても応答がなければ、あきらめて failed にする（やり直さない）。 */
export async function sendConfigurationCommand(baseUrl: string, command: ConfigurationCommand, timeoutMs: number): Promise<ConfigurationResult> {
  const { path, body } = configurationRequest(command);
  const result = await postOperation(baseUrl, path, body, timeoutMs, SERVER_RESULTS);
  return result === 'refused' ? 'failed' : result;
}

/** 画面に出す、操作の結果の知らせ（受け付けたときは null）。 */
export function configurationNotice(command: ConfigurationCommand, result: ConfigurationResult): string | null {
  const what = subjectOf(command);
  switch (result) {
    case 'saved':
      return null;
    case 'alreadyRegistered':
      return command.kind === 'addId'
        ? `${command.id} は、もうほかの品種に対応させてあるか、消耗品の記録にあります。`
        : `その名前の${what}は、もう登録してあります。`;
    case 'notFound':
      return `${what}が見つかりませんでした。ほかの画面で変えられたかもしれません。表示を確かめてください。`;
    case 'inUse':
      return command.kind === 'deleteProcessKind'
        ? 'その加工の種類の品種があるので、消せませんでした。'
        : 'その品種は、消耗品の記録かストッカーの設定で使っているので、変えられませんでした。';
    case 'failed':
      return '保存できませんでした。サーバーから応答がありません。表示を確かめてください。';
    default:
      throw new Error(`知らない結果です: ${JSON.stringify(result satisfies never)}`);
  }
}

function subjectOf(command: ConfigurationCommand): string {
  switch (command.kind) {
    case 'addProcessKind':
    case 'editProcessKind':
    case 'deleteProcessKind':
      return '加工の種類';
    case 'addVariety':
    case 'editVariety':
    case 'deleteVariety':
      return '品種';
    case 'addId':
    case 'removeId':
      return '入庫の予定';
    default:
      throw new Error(`知らない操作です: ${JSON.stringify(command satisfies never)}`);
  }
}
