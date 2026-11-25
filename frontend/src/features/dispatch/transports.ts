import type { AgvId } from '../fleet/agv';
import type { Permissions } from '../auth/permissions';

/** 搬送の段階（タブ）。 */
export type TransportStatus = { readonly stage: 'queued' } | { readonly stage: 'running' } | { readonly stage: 'suspended' };

export type TransportStage = TransportStatus['stage'];

/** 搬送の一覧の1行。 */
export interface Transport {
  /** 搬送の番号（#12 の 12）。番号は、頼まれた順に付く。 */
  readonly number: number;
  readonly status: TransportStatus;
  /** 頼んだ側から聞いた搬送の種類（届ける、回収する など）。 */
  readonly kind: string;
  readonly from: string;
  readonly to: string;
  /** 運ぶもの（画面に出す文）。 */
  readonly cargo: string;
  readonly neededBy: Date;
  readonly agv: AgvId | null;
  /** いまの段階（割り当て待ち、搬送先へ移動 など）と、その補足。 */
  readonly step: string;
  readonly stepNote: string;
}

/**
 * 選んだタブの搬送を、必要時刻が早い順に並べる。同じなら、搬送の番号の順（docs/screens.md）。
 * 番号は頼まれた順に付くので、待ち行列では頼まれた順（割り当てる順）と同じになる。
 */
export function transportsOf(transports: readonly Transport[], stage: TransportStage): Transport[] {
  return transports.filter((t) => t.status.stage === stage).sort((a, b) => a.neededBy.getTime() - b.neededBy.getTime() || a.number - b.number);
}

/** 開いたときのタブ：中断した搬送があればそれを、なければ走っている搬送を出す。 */
export function initialTransportTab(transports: readonly Transport[]): TransportStage {
  return transports.some((t) => t.status.stage === 'suspended') ? 'suspended' : 'running';
}

const NO_PERMISSION = '搬送の取りやめとやり直しは、運用以上のロールで使えます。';
const OFFLINE = 'サーバーとつながっていないので、取りやめられません。';
const SUSPENDED = '中断した搬送のやり直しと取りやめは、まだできません（異常のときの扱いを決めてから作ります）。';

/** 取りやめを押せない理由（押せるなら null）。中断した搬送は、まだ取りやめられない（未決事項 #10）。 */
export function cancelBlockedReason(transport: Transport, permissions: Permissions, offline: boolean): string | null {
  if (!permissions.has('transportCancel')) {
    return NO_PERMISSION;
  }
  if (transport.status.stage === 'suspended') {
    return SUSPENDED;
  }
  return offline ? OFFLINE : null;
}

/** やり直すを押せない理由（押せるなら null）。中断した搬送のやり直しは、まだ作っていない（未決事項 #10）。 */
export function retryBlockedReason(permissions: Permissions): string {
  return permissions.has('transportCancel') ? SUSPENDED : NO_PERMISSION;
}

/** 権限がないことの理由（画面の上に1つだけ出す）。権限があれば null。 */
export function permissionNote(permissions: Permissions): string | null {
  return permissions.has('transportCancel') ? null : NO_PERMISSION;
}
