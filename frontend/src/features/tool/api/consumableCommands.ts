import { postSessionOperation } from '../../../shared/api/postCommand';
import type { Condition } from '../station';
import type { ConsumableLocation } from '../consumables';

/** 在庫の手入力で送る記録。所在は、ステーションか、ストッカーのスロット。 */
export interface ManualRecord {
  readonly id: string;
  readonly variety: string;
  readonly condition: Condition;
  readonly location: ConsumableLocation;
}

/** 画面から送る消耗品の操作（装置とストッカー、ステーションの在庫の画面）。 */
export type ConsumableCommand =
  | { readonly kind: 'stockerSetting'; readonly device: string; readonly counts: readonly { readonly variety: string; readonly count: number }[] }
  | { readonly kind: 'ship'; readonly consumable: string }
  | { readonly kind: 'correct'; readonly consumable: string; readonly variety: string; readonly condition: Condition }
  | { readonly kind: 'designate'; readonly consumable: string; readonly device: string }
  | { readonly kind: 'cancelDesignation'; readonly consumable: string }
  | { readonly kind: 'register'; readonly record: ManualRecord }
  | { readonly kind: 'edit'; readonly record: ManualRecord }
  | { readonly kind: 'delete'; readonly consumable: string };

/**
 * 操作の結果。
 * - saved・requested・corrected・designated・canceled：受け付けた。
 * - exceedsSlots：個数の合計がスロットの数を超えている。notAtStation：ステーションにない。
 *   inAssignedTransport：AGV を割り当て済みの、届ける搬送に入っている。notNeedingCheck：もう要確認ではない。
 *   notDesignatable：指名できるものではない。alreadyRegistered・notFound・notEditable・slotTaken：在庫の手入力を断った。どれも、何も変えていない。
 * - failed：応答がない、または思わない応答（届いたかわからない。やり直さない）。
 */
export type ConsumableCommandResult =
  | 'forbidden'
  | 'saved'
  | 'exceedsSlots'
  | 'requested'
  | 'notAtStation'
  | 'inAssignedTransport'
  | 'corrected'
  | 'notNeedingCheck'
  | 'designated'
  | 'notDesignatable'
  | 'canceled'
  | 'alreadyRegistered'
  | 'notFound'
  | 'notEditable'
  | 'slotTaken'
  | 'failed';

const conditionText = (condition: Condition) => (condition === '新品' ? 'new' : 'used');

/** 手入力の所在を、サーバーへ送る形にする。ステーションとストッカーのスロットのほかは、送れない（呼ぶ側の誤り）。 */
function locationBody(location: ConsumableLocation): Record<string, unknown> {
  switch (location.kind) {
    case 'station':
      return { kind: 'station', station: location.station };
    case 'slot':
      return { kind: 'slot', device: location.device, stocker: location.stocker, slot: location.slot };
    default:
      throw new Error(`手入力で選べる所在は、ステーションか、ストッカーのスロットです: ${JSON.stringify(location)}`);
  }
}

/** 操作を、サーバーの HTTP の窓口へ送る形にする。 */
export function consumableCommandRequest(command: ConsumableCommand): { readonly path: string; readonly body: Record<string, unknown> } {
  switch (command.kind) {
    case 'stockerSetting':
      return { path: `/devices/${encodeURIComponent(command.device)}/stocker-setting`, body: { counts: command.counts } };
    case 'ship':
      return { path: `/consumables/${encodeURIComponent(command.consumable)}/ship`, body: {} };
    case 'correct':
      return {
        path: `/consumables/${encodeURIComponent(command.consumable)}/correct`,
        body: { variety: command.variety, condition: conditionText(command.condition) },
      };
    case 'designate':
      return { path: `/consumables/${encodeURIComponent(command.consumable)}/designate`, body: { device: command.device } };
    case 'cancelDesignation':
      return { path: `/consumables/${encodeURIComponent(command.consumable)}/cancel-designation`, body: {} };
    case 'register':
      return {
        path: '/consumables',
        body: { id: command.record.id, variety: command.record.variety, condition: conditionText(command.record.condition), location: locationBody(command.record.location) },
      };
    case 'edit':
      return {
        path: `/consumables/${encodeURIComponent(command.record.id)}/edit`,
        body: { variety: command.record.variety, condition: conditionText(command.record.condition), location: locationBody(command.record.location) },
      };
    case 'delete':
      return { path: `/consumables/${encodeURIComponent(command.consumable)}/delete`, body: {} };
    default:
      throw new Error(`知らない操作です: ${JSON.stringify(command satisfies never)}`);
  }
}

/** 操作を送る。timeoutMs を過ぎても応答がなければ、あきらめて failed にする（やり直さない）。 */
export async function sendConsumableCommand(baseUrl: string, command: ConsumableCommand, timeoutMs: number): Promise<ConsumableCommandResult> {
  const { path, body } = consumableCommandRequest(command);
  switch (command.kind) {
    case 'stockerSetting':
      return postSessionOperation(baseUrl, path, timeoutMs, body, ['saved', 'exceedsSlots']);
    case 'ship':
      return postSessionOperation(baseUrl, path, timeoutMs, body, ['requested', 'notAtStation', 'inAssignedTransport']);
    case 'correct':
      return postSessionOperation(baseUrl, path, timeoutMs, body, ['corrected', 'notNeedingCheck']);
    case 'designate':
      return postSessionOperation(baseUrl, path, timeoutMs, body, ['designated', 'notDesignatable']);
    case 'cancelDesignation':
      return postSessionOperation(baseUrl, path, timeoutMs, body, ['canceled', 'inAssignedTransport']);
    case 'register':
    case 'edit':
    case 'delete':
      return postSessionOperation(baseUrl, path, timeoutMs, body, ['saved', 'alreadyRegistered', 'notFound', 'notEditable', 'inAssignedTransport', 'slotTaken']);
  }
}

/** 画面に出す、操作の結果の知らせ（受け付けたときは null）。同じ結果でも、操作によって文を変える。 */
export function consumableNotice(kind: ConsumableCommand['kind'], result: ConsumableCommandResult): string | null {
  switch (result) {
    case 'forbidden':
      if (kind === 'register' || kind === 'edit' || kind === 'delete') return '在庫を手入力する権限がありません。';
      return kind === 'stockerSetting' ? 'ストッカーの設定を変更する権限がありません。' : 'この消耗品を操作する権限がありません。';
    case 'saved':
    case 'requested':
    case 'corrected':
    case 'designated':
    case 'canceled':
      return null;
    case 'alreadyRegistered':
      return 'その ID は、もう登録してあります。';
    case 'notFound':
      return `記録にないので、${kind === 'delete' ? '消せませんでした' : '直せませんでした'}（ほかの人が消したか、出庫したなど）。表示を確かめてください。`;
    case 'notEditable':
      return 'AGV の中や、加工部・受け渡し部にあるもの、出庫を頼んだものは、直したり消したりできません。表示を確かめてください。';
    case 'slotTaken':
      return '選んだスロットには、ほかの消耗品があります。別の所在を選んでください。';
    case 'exceedsSlots':
      return '個数の合計がスロットの数を超えているので、保存できませんでした。';
    case 'notAtStation':
      return 'ステーションにないので、出庫できませんでした。表示を確かめてください。';
    case 'inAssignedTransport':
      return kind === 'cancelDesignation'
        ? '届ける搬送に AGV を割り当て済みなので、指名を取り消せませんでした。'
        : kind === 'edit' || kind === 'delete'
          ? '搬送に AGV を割り当て済みなので、直したり消したりできませんでした。'
          : '届ける搬送に AGV を割り当て済みなので、出庫できませんでした。';
    case 'notDesignatable':
      return '指名できませんでした。指名できるのは、ステーションにある、要確認でない新品で、届ける予約も出庫の頼みも指名もないものです。表示を確かめてください。';
    case 'notNeedingCheck':
      return 'もう要確認ではないので、直しませんでした（ほかの人が直したか、出庫したなど）。表示を確かめてください。';
    case 'failed':
      return 'サーバーから応答がなく、届いたかわかりません。表示を確かめてください。';
    default:
      throw new Error(`知らない結果です: ${JSON.stringify(result satisfies never)}`);
  }
}
