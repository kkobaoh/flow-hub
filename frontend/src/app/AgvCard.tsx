import type { ReactNode } from 'react';
import { indicatorLight, lightLabel, type AgvId, type AgvPosition, type AgvStatus } from '../map/agv';
import type { Heading } from '../map/model';
import { controlOptions, controlStatusLabel, type AgvOperation, type ControlStatus } from './controlStatus';
import type { ManualActivity } from './manual';
import { CardHeader } from './CardHeader';
import type { Permissions } from './permissions';

/** カードに出す AGV の詳細。 */
export interface AgvDetail {
  readonly id: AgvId;
  /** マップの印に書く短い名前。 */
  readonly label: string;
  readonly position: AgvPosition;
  readonly status: AgvStatus;
  readonly control: ControlStatus;
  readonly operation: AgvOperation;
  /** 割り当て済みの搬送（なければ null）。 */
  readonly transport: string | null;
  /** 充電残量（%）。まだ一度も届いていなければ null。 */
  readonly batteryPercent: number | null;
  /** マニュアルで、今走らせているもの（なければ 'none'）。 */
  readonly manualActivity?: ManualActivity;
}

export interface AgvCardProps {
  readonly agv: AgvDetail;
  /** いるパネルに充電ピンがあるか（標示灯の色を決めるため）。 */
  readonly onChargingPin: boolean;
  readonly permissions: Permissions;
  readonly onClose: () => void;
  readonly onEmergencyStop: (id: AgvId) => void;
  /** needsPosition は、切り離しから戻すので、位置の指定が要るか。 */
  readonly onChangeControl: (id: AgvId, status: ControlStatus, needsPosition: boolean) => void;
  readonly onAlarmClear: (id: AgvId) => void;
  readonly onPositionReset: (id: AgvId) => void;
  /** マニュアル操作の欄（ManualPanel）。保守のロールで、AGV がマニュアルのときだけ出す。 */
  readonly manual?: ReactNode;
  /** 一覧から開いたときだけ渡す（一覧に戻るボタンを出す）。 */
  readonly onBack?: (() => void) | undefined;
}

const LIGHT_BG = {
  moving: 'bg-light-moving',
  waiting: 'bg-light-waiting',
  charging: 'bg-light-charging',
  chargeFailed: 'bg-light-charge-failed',
} as const;

const OPERATION_LABEL: Record<Exclude<AgvOperation, 'idle'>, string> = {
  traveling: '走行動作中',
  emergencyStopped: '緊急停止中',
  fault: '異常',
  awaitingPositionReset: '位置リセット待ち',
  positionUnknown: '位置がわからない',
};

const CARDINAL: Partial<Record<number, string>> = { 0: '北', 90: '東', 180: '南', 270: '西' };

function headingText(h: Heading): string {
  const cardinal = CARDINAL[h];
  return cardinal ? `${h}°（${cardinal}）` : `${h}°`;
}

function positionText(position: AgvPosition): string {
  switch (position.kind) {
    case 'onMap':
      return `${position.panel}　${headingText(position.heading)}`;
    case 'offMap':
      return `${position.panel}（マップにない）　${headingText(position.heading)}`;
    case 'unknown':
      return 'わからない';
    default:
      throw new Error(`知らない位置です: ${JSON.stringify(position satisfies never)}`);
  }
}

const NO_COMMAND = '切り離し中の AGV には、コマンドを送りません。';

const optionButton =
  'min-h-11 rounded-lg text-sm font-bold focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-focus disabled:cursor-not-allowed';
const plainButton = `${optionButton} border border-control-border bg-white text-graphite disabled:border-disabled-border disabled:bg-ground disabled:text-disabled-text`;

/** AGV のカード。詳細と、その AGV への操作（緊急停止、コントロールステータス、復旧）をまとめて出す。 */
export function AgvCard({
  agv,
  onChargingPin,
  permissions,
  onClose,
  onEmergencyStop,
  onChangeControl,
  onAlarmClear,
  onPositionReset,
  manual,
  onBack,
}: AgvCardProps) {
  const light = indicatorLight(agv.status, onChargingPin);
  const stateText = agv.operation === 'idle' ? lightLabel(light.color) : OPERATION_LABEL[agv.operation];
  const options = controlOptions({
    current: agv.control,
    operation: agv.operation,
    hasAssignedTransport: agv.transport !== null,
    permissions,
  });
  const reasons = [...new Set(options.flatMap((o) => (o.reason ? [`${o.label}：${o.reason}`] : [])))];
  const detached = agv.control === 'detached';
  const canStop = permissions.has('emergencyStop') && !detached;
  const stopNote = detached ? NO_COMMAND : permissions.has('emergencyStop') ? null : '緊急停止は、運用以上のロールで使えます。';
  const canRecover = permissions.has('recovery');

  return (
    <section aria-labelledby={`agv-${agv.id}`} className="flex flex-col bg-paper text-graphite">
      <div className="sticky top-0 z-1">
        <CardHeader titleId={`agv-${agv.id}`} title={agv.id} onClose={onClose} onBack={onBack} />
        <div className="border-b border-line bg-paper px-5 py-3">
          <button
            type="button"
            disabled={!canStop}
            onClick={() => onEmergencyStop(agv.id)}
            className="flex min-h-16 w-full items-center gap-3.5 rounded-[10px] border-2 border-graphite bg-estop-plate px-3.5 text-lg font-bold text-graphite focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-focus disabled:cursor-not-allowed disabled:opacity-50"
          >
            <span aria-hidden="true" className="size-12 shrink-0 rounded-full bg-estop shadow-[inset_0_-4px_0_rgba(0,0,0,0.3),0_0_0_3px_var(--color-graphite)]" />
            緊急停止
          </button>
          {stopNote && <p className="m-0 mt-2 text-xs text-ink-muted">{stopNote}</p>}
        </div>
      </div>

      <div className="flex flex-col gap-3 px-5 py-4">
        <div className="flex items-center gap-2 text-base font-bold">
          <span aria-hidden="true" className={`size-3.5 rounded-full ring-1 ring-graphite ${LIGHT_BG[light.color]}${light.blinking ? ' animate-agv-blink' : ''}`} />
          {stateText}
        </div>
        <dl className="m-0 grid grid-cols-[max-content_1fr] gap-x-4 gap-y-1.5 text-sm">
          <dt className="text-ink-muted">コントロール</dt>
          <dd className="m-0 font-bold">{controlStatusLabel[agv.control]}</dd>
          <dt className="text-ink-muted">位置</dt>
          <dd className="m-0">
            {positionText(agv.position)}
          </dd>
          <dt className="text-ink-muted">搬送</dt>
          <dd className="m-0">{agv.transport ?? 'なし'}</dd>
          <dt className="text-ink-muted">充電残量</dt>
          <dd className="m-0 flex items-center gap-2">
            {agv.batteryPercent === null ? (
              '不明'
            ) : (
              <>
                <span className="h-2 flex-1 overflow-hidden rounded bg-track">
                  <span className="block h-2 bg-running" style={{ width: `${agv.batteryPercent}%` }} />
                </span>
                <span className="font-number text-base font-semibold">{agv.batteryPercent}%</span>
              </>
            )}
          </dd>
        </dl>
      </div>

      <div className="flex flex-col gap-2 border-t border-line px-5 py-4">
        <h3 className="m-0 text-sm font-bold">コントロールステータス</h3>
        <div className="grid grid-cols-2 gap-2">
          {options.map((o) => (
            <button
              key={o.status}
              type="button"
              aria-pressed={o.current}
              disabled={!o.current && !o.allowed}
              title={o.reason ?? undefined}
              onClick={() => o.allowed && onChangeControl(agv.id, o.status, o.needsPosition)}
              className={o.current ? `${optionButton} border-2 border-graphite bg-graphite text-white` : plainButton}
            >
              {o.label}
            </button>
          ))}
        </div>
        {reasons.length > 0 && (
          <ul className="m-0 flex list-none flex-col gap-1 p-0 text-xs text-ink-muted">
            {reasons.map((r) => (
              <li key={r}>{r}</li>
            ))}
          </ul>
        )}
      </div>

      {canRecover ? (
        <div className="flex flex-col gap-2 border-t border-line px-5 pt-4 pb-6">
          <h3 className="m-0 text-sm font-bold">復旧</h3>
          <div className="grid grid-cols-2 gap-2">
            <button
              type="button"
              disabled={detached || agv.control === 'locked'}
              title={agv.control === 'locked' ? 'ロック中は、アラームクリアを送れません（AGV が続きを走り出すため）。' : undefined}
              onClick={() => onAlarmClear(agv.id)}
              className={plainButton}
            >
              アラームクリア
            </button>
            <button type="button" disabled={detached} onClick={() => onPositionReset(agv.id)} className={plainButton}>
              位置リセット
            </button>
          </div>
          {agv.control === 'locked' && <p className="m-0 text-xs text-ink-muted">ロック中は、アラームクリアを送れません（AGV が続きを走り出すため）。</p>}
          {detached && <p className="m-0 text-xs text-ink-muted">{NO_COMMAND}</p>}
        </div>
      ) : null}
      {canRecover && permissions.has('manual') ? (
        agv.control === 'manual' ? (
          manual
        ) : (
          <p className="m-0 border-t border-line px-5 pt-3.5 pb-6 text-[13px] text-ink-muted">マニュアルに切り替えると、ここから行き先や1つずつの動作を送れます。</p>
        )
      ) : null}
      {canRecover ? null : (
        <p className="m-0 border-t border-line px-5 pt-3.5 pb-6 text-[13px] text-ink-muted">
          アラームクリア、位置リセット、マニュアル操作は、保守以上のロールで使えます。
        </p>
      )}
    </section>
  );
}
