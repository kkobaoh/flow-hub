import type { Permissions } from './permissions';

/** コントロールステータス（docs/domain.md「コントロールステータス」）。 */
export type ControlStatus = 'auto' | 'assignmentPaused' | 'manual' | 'locked' | 'detached';

/** AGV の動きの状態。 */
export type AgvOperation = 'idle' | 'traveling' | 'emergencyStopped' | 'fault' | 'awaitingPositionReset';

export const controlStatusLabel: Record<ControlStatus, string> = {
  auto: 'オート',
  assignmentPaused: '割り当て停止',
  manual: 'マニュアル',
  locked: 'ロック',
  detached: '切り離し',
};

const ORDER: readonly ControlStatus[] = ['auto', 'assignmentPaused', 'manual', 'locked', 'detached'];
/** 保守の操作になるステータス（ここへ切り替えるのも、ここから切り替えるのも）。 */
const ADVANCED: ReadonlySet<ControlStatus> = new Set(['manual', 'locked', 'detached']);

export interface ControlInput {
  readonly current: ControlStatus;
  readonly operation: AgvOperation;
  readonly hasAssignedTransport: boolean;
  readonly permissions: Permissions;
}

export interface ControlOption {
  readonly status: ControlStatus;
  readonly label: string;
  /** 今のステータスか。 */
  readonly current: boolean;
  /** 切り替えられるか。今のステータスは false。 */
  readonly allowed: boolean;
  /** 切り替えられない理由（ユーザーに見せる文）。 */
  readonly reason: string | null;
  /** 切り離しから戻すので、位置（パネルと角度）の指定が要るか。 */
  readonly needsPosition: boolean;
}

function denialReason(target: ControlStatus, input: ControlInput): string | null {
  const advanced = ADVANCED.has(target) || ADVANCED.has(input.current);
  if (advanced ? !input.permissions.has('controlAdvanced') : !input.permissions.has('controlBasic')) {
    return advanced ? '保守以上のロールで切り替えられます。' : '運用以上のロールで切り替えられます。';
  }
  if (input.current === 'detached') {
    return null; // 戻すときは、位置を指定してもらう
  }
  if (ADVANCED.has(target) && input.hasAssignedTransport) {
    return '割り当て済みの搬送があります。先に割り当て停止で搬送を終えるか、搬送を取りやめてください。';
  }
  if (target === 'detached' && input.current !== 'locked') {
    return 'ロック中のときだけ切り替えられます。先にロックにしてください。';
  }
  if ((target === 'manual' || target === 'locked') && input.operation !== 'idle' && input.operation !== 'emergencyStopped') {
    return input.operation === 'traveling' ? '走っているあいだは切り替えられません。' : '待機中か緊急停止中のときだけ切り替えられます。';
  }
  return null;
}

/**
 * コントロールステータスの、それぞれに切り替えられるかと、できない理由を返す。
 * 切り替えの条件は docs/domain.md、ロールごとの操作は docs/screens.md のとおり。
 */
export function controlOptions(input: ControlInput): ControlOption[] {
  return ORDER.map((status) => {
    const current = status === input.current;
    const reason = current ? null : denialReason(status, input);
    return {
      status,
      label: controlStatusLabel[status],
      current,
      allowed: !current && reason === null,
      reason,
      needsPosition: !current && input.current === 'detached',
    };
  });
}
