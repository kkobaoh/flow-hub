import type { AgvId } from '../map/agv';
import type { Permissions } from './permissions';

/** 搬送の段階（タブ）。中断した搬送だけが、位置リセットが済んだかを持つ。 */
export type TransportStatus =
  | { readonly stage: 'queued' }
  | { readonly stage: 'running' }
  | { readonly stage: 'suspended'; readonly positionResetDone: boolean };

export type TransportStage = TransportStatus['stage'];

/** 搬送の一覧の1行。 */
export interface Transport {
  /** 搬送の番号（#12 の 12）。 */
  readonly number: number;
  readonly status: TransportStatus;
  /** 頼んだ側が付けた搬送の種類（届ける、回収する など）。 */
  readonly kind: string;
  readonly from: string;
  readonly to: string;
  /** 運ぶもの（画面に出す文）。 */
  readonly cargo: string;
  readonly neededBy: Date;
  readonly requestedAt: Date;
  readonly agv: AgvId | null;
  /** いまの段階（割り当て待ち、搬送先へ移動 など）と、その補足。 */
  readonly step: string;
  readonly stepNote: string;
}

/** 選んだタブの搬送を、必要時刻が早い順に並べる。同じなら、待ち行列は頼まれた順、ほかは番号の順（docs/screens.md）。 */
export function transportsOf(transports: readonly Transport[], stage: TransportStage): Transport[] {
  const tie = stage === 'queued' ? (a: Transport, b: Transport) => a.requestedAt.getTime() - b.requestedAt.getTime() : (a: Transport, b: Transport) => a.number - b.number;
  return transports.filter((t) => t.status.stage === stage).sort((a, b) => a.neededBy.getTime() - b.neededBy.getTime() || tie(a, b));
}

/** 開いたときのタブ：中断した搬送があればそれを、なければ走っている搬送を出す。 */
export function initialTransportTab(transports: readonly Transport[]): TransportStage {
  return transports.some((t) => t.status.stage === 'suspended') ? 'suspended' : 'running';
}

const NO_PERMISSION = '搬送の取りやめとやり直しは、運用以上のロールで使えます。';

/** 取りやめを押せない理由（押せるなら null）。 */
export function cancelBlockedReason(permissions: Permissions): string | null {
  return permissions.has('transportCancel') ? null : NO_PERMISSION;
}

/** やり直すを押せない理由（押せるなら null）。位置リセットが済むまでは押せない。 */
export function retryBlockedReason(transport: Transport, permissions: Permissions): string | null {
  if (!permissions.has('transportCancel')) {
    return NO_PERMISSION;
  }
  if (transport.status.stage === 'suspended' && !transport.status.positionResetDone) {
    return `${transport.agv ?? 'AGV'} の位置リセットのあとに、やり直せます。`;
  }
  return null;
}

/** 時刻を「10:02」の形にする。 */
export function clockOf(time: Date): string {
  return `${String(time.getHours()).padStart(2, '0')}:${String(time.getMinutes()).padStart(2, '0')}`;
}
