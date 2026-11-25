import { postOperation } from '../../../shared/api/postCommand';
import type { ManualStepCommand } from '../ManualPanel';
import type { ManualOutcome } from '../manual';
import type { ControlStatus } from '../controlStatus';
import type { AgvId } from '../agv';
import type { Heading, PanelId } from '../../routing/model';

/** AGV の位置（パネルと向き）。位置リセットと、切り離しから戻すときに指定する。 */
export interface AgvPose {
  readonly panel: PanelId;
  readonly heading: Heading;
}

/** 画面から AGV へ送る操作。 */
export type AgvCommand =
  | { readonly kind: 'emergencyStop' }
  | { readonly kind: 'alarmClear' }
  | { readonly kind: 'positionReset'; readonly position: AgvPose }
  /** position は、切り離しから戻すときだけ指定する（ほかは null）。 */
  | { readonly kind: 'control'; readonly status: ControlStatus; readonly position: AgvPose | null }
  | { readonly kind: 'manualMove'; readonly destination: PanelId; readonly arrival: Heading | null }
  | { readonly kind: 'manualStep'; readonly step: ManualStepCommand }
  | { readonly kind: 'manualCarrier'; readonly action: 'raise' | 'lower' };

/**
 * 操作の結果。
 * - accepted：受け付けた。alreadyThere・noRoute・blocked・arrivalHeadingNotAllowed・notAllowed は、マニュアル操作の結果。
 * - reserved：位置のパネルを、ほかの AGV が予約している。
 * - refused：今の AGV の状態では、受け付けられなかった（何も変えていない）。
 * - failed：応答がない、または思わない応答（届いたかわからない。やり直さない）。
 */
export type AgvCommandResult =
  | 'accepted'
  | 'alreadyThere'
  | 'noRoute'
  | 'blocked'
  | 'arrivalHeadingNotAllowed'
  | 'notAllowed'
  | 'reserved'
  | 'refused'
  | 'failed';

const SERVER_RESULTS: ReadonlySet<Exclude<AgvCommandResult, 'refused' | 'failed'>> = new Set([
  'accepted',
  'alreadyThere',
  'noRoute',
  'blocked',
  'arrivalHeadingNotAllowed',
  'notAllowed',
  'reserved',
]);

/** 操作を、サーバーの HTTP の窓口（/agvs/{id}/…）へ送る形にする。 */
export function agvCommandRequest(id: AgvId, command: AgvCommand): { readonly path: string; readonly body: Record<string, unknown> } {
  const base = `/agvs/${encodeURIComponent(id)}`;
  switch (command.kind) {
    case 'emergencyStop':
      return { path: `${base}/emergency-stop`, body: {} };
    case 'alarmClear':
      return { path: `${base}/alarm-clear`, body: {} };
    case 'positionReset':
      return { path: `${base}/position-reset`, body: { panel: command.position.panel, heading: command.position.heading } };
    case 'control':
      return {
        path: `${base}/control`,
        body: command.position === null ? { status: command.status } : { status: command.status, panel: command.position.panel, heading: command.position.heading },
      };
    case 'manualMove':
      return {
        path: `${base}/manual/move`,
        body: command.arrival === null ? { destination: command.destination } : { destination: command.destination, arrival: command.arrival },
      };
    case 'manualStep': {
      const step = command.step;
      return { path: `${base}/manual/step`, body: step.kind === 'rotate' || step.kind === 'freeRotate' ? { kind: step.kind, heading: step.heading } : { kind: step.kind } };
    }
    case 'manualCarrier':
      return { path: `${base}/manual/carrier`, body: { action: command.action } };
    default:
      throw new Error(`知らない操作です: ${JSON.stringify(command satisfies never)}`);
  }
}

/** 操作を送る。timeoutMs を過ぎても応答がなければ、あきらめて failed にする（やり直さない）。 */
export function sendAgvCommand(baseUrl: string, id: AgvId, command: AgvCommand, timeoutMs: number): Promise<AgvCommandResult> {
  const { path, body } = agvCommandRequest(id, command);
  return postOperation(baseUrl, path, body, timeoutMs, SERVER_RESULTS);
}

/** カードに出す、操作の結果の知らせ（受け付けたときは null）。マニュアル操作は、マニュアル操作の欄が出す。 */
export function commandNotice(result: AgvCommandResult): string | null {
  switch (result) {
    case 'accepted':
      return null;
    case 'reserved':
      return 'そのパネルは、ほかの AGV が予約しています。別のパネルを選ぶか、空くのを待ってください。';
    case 'refused':
      return '今の AGV の状態では、受け付けられませんでした。AGV の様子を確かめてください。';
    case 'failed':
      return 'サーバーから応答がなく、届いたかわかりません。AGV の様子を確かめてください。';
    case 'alreadyThere':
    case 'noRoute':
    case 'blocked':
    case 'arrivalHeadingNotAllowed':
    case 'notAllowed':
      return null; // マニュアル操作の結果（マニュアル操作の欄が出す）
    default:
      throw new Error(`知らない結果です: ${JSON.stringify(result satisfies never)}`);
  }
}

/** 操作の結果を、マニュアル操作の欄の言葉にする。マニュアル操作で返らないはずのもの（reserved）は failed にする。 */
export function toManualOutcome(result: AgvCommandResult): ManualOutcome {
  return result === 'reserved' ? 'failed' : result;
}
