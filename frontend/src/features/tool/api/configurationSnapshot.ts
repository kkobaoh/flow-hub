import { postOperation } from '../../../shared/api/postCommand';
import { parseVersion, type Versioned } from '../../../shared/realtime/versioned';
import type { DeviceConfig, ProcessKind, StationConfig, Variety } from '../config';

/** サーバーから届く、消耗品の構成（加工の種類、品種、装置、ステーション）。変わるたびに番号が進む。 */
export interface ConfigurationSnapshot extends Versioned {
  /** 加工の種類（ID の順）。 */
  readonly processKinds: readonly ProcessKind[];
  /** 品種（ID の順）。 */
  readonly varieties: readonly Variety[];
  /** 装置（ID の順）。 */
  readonly devices: readonly DeviceConfig[];
  /** ステーション（ID の順）。 */
  readonly stations: readonly StationConfig[];
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

function positiveInteger(value: unknown, what: string): number {
  if (typeof value !== 'number' || !Number.isInteger(value) || value < 1) {
    throw new TypeError(`${what}が正しくありません（${String(value)}）。`);
  }
  return value;
}

function flag(value: unknown, what: string): boolean {
  if (typeof value !== 'boolean') {
    throw new TypeError(`${what}が正しくありません。`);
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
    devices: records(value.devices, '装置').map((d) => {
      const id = text(d.id, '装置の ID');
      if (!Array.isArray(d.slots) || d.slots.length === 0) {
        throw new TypeError(`装置 ${id} のストッカーが正しくありません。`);
      }
      const slots = d.slots.map((n: unknown) => positiveInteger(n, `装置 ${id} のスロットの数`));
      // ふさがっているスロットを、ストッカーごとの番号の並びにする
      const occupied: number[][] = slots.map(() => []);
      for (const slot of records(d.occupied, `装置 ${id} のふさがっているスロット`)) {
        const stocker = positiveInteger(slot.stocker, `装置 ${id} のストッカーの番号`);
        const number = positiveInteger(slot.slot, `装置 ${id} のスロットの番号`);
        if (stocker > slots.length) {
          throw new TypeError(`装置 ${id} に、ストッカー${stocker} はありません。`);
        }
        occupied[stocker - 1]!.push(number);
      }
      return { id, panel: text(d.panel, `装置 ${id} の受け渡しパネル`), slots, occupied, busy: flag(d.inUse, `装置 ${id} の、使われているか`) };
    }),
    stations: records(value.stations, 'ステーション').map((s) => {
      const id = text(s.id, 'ステーションの ID');
      return { id, panel: text(s.panel, `ステーション ${id} の受け渡しパネル`), busy: flag(s.inUse, `ステーション ${id} の、使われているか`) };
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
  | { readonly kind: 'removeId'; readonly id: string }
  | { readonly kind: 'addDevice' | 'editDevice'; readonly device: { readonly id: string; readonly panel: string; readonly slots: readonly number[] } }
  | { readonly kind: 'deleteDevice'; readonly id: string }
  | { readonly kind: 'addStation' | 'editStation'; readonly station: { readonly id: string; readonly panel: string } }
  | { readonly kind: 'deleteStation'; readonly id: string };

/**
 * 操作の結果（サーバーの ConfigurationEndpoints と同じ言葉）。
 * - saved：受け付けた。alreadyRegistered・notFound・inUse・panelUnavailable・slotsOccupied・exceedsStockerSetting・stationLimit：断った（何も変えていない）。
 * - failed：応答がない、または思わない応答（やり直さない）。
 */
export type ConfigurationResult =
  | 'saved'
  | 'alreadyRegistered'
  | 'notFound'
  | 'inUse'
  | 'panelUnavailable'
  | 'slotsOccupied'
  | 'exceedsStockerSetting'
  | 'stationLimit'
  | 'failed';

const SERVER_RESULTS: ReadonlySet<ConfigurationResult> = new Set([
  'saved',
  'alreadyRegistered',
  'notFound',
  'inUse',
  'panelUnavailable',
  'slotsOccupied',
  'exceedsStockerSetting',
  'stationLimit',
] as const);

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
    case 'addDevice':
      return { path: '/configuration/devices', body: { id: command.device.id, panel: command.device.panel, slots: command.device.slots } };
    case 'editDevice':
      return { path: `/configuration/devices/${segment(command.device.id)}/edit`, body: { panel: command.device.panel, slots: command.device.slots } };
    case 'deleteDevice':
      return { path: `/configuration/devices/${segment(command.id)}/delete`, body: {} };
    case 'addStation':
      return { path: '/configuration/stations', body: { id: command.station.id, panel: command.station.panel } };
    case 'editStation':
      return { path: `/configuration/stations/${segment(command.station.id)}/edit`, body: { panel: command.station.panel } };
    case 'deleteStation':
      return { path: `/configuration/stations/${segment(command.id)}/delete`, body: {} };
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
      switch (command.kind) {
        case 'deleteProcessKind':
          return 'その加工の種類の品種があるので、消せませんでした。';
        case 'editDevice':
        case 'deleteDevice':
        case 'editStation':
        case 'deleteStation':
          return `その${what}は、中に消耗品があるか、かかわる搬送があるので、${command.kind.startsWith('delete') ? '消せません' : '受け渡しパネルを変えられません'}でした。`;
        default:
          return 'その品種は、消耗品の記録かストッカーの設定で使っているので、変えられませんでした。';
      }
    case 'panelUnavailable':
      return 'その受け渡しパネルは、公開中のマップにないか、ほかの装置かステーションが使っています。表示を確かめてください。';
    case 'slotsOccupied':
      return '減らすストッカーかスロットに、消耗品があるか、届ける予定があるので、減らせませんでした。';
    case 'exceedsStockerSetting':
      return 'ストッカーの設定の個数の合計より、スロットが少なくなるので、減らせませんでした。先にストッカーの設定を減らしてください。';
    case 'stationLimit':
      return 'ステーションは、1台だけ登録できます（ステーションの選び方のルールが決まるまで）。';
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
    case 'addDevice':
    case 'editDevice':
    case 'deleteDevice':
      return '装置';
    case 'addStation':
    case 'editStation':
    case 'deleteStation':
      return 'ステーション';
    default:
      throw new Error(`知らない操作です: ${JSON.stringify(command satisfies never)}`);
  }
}
