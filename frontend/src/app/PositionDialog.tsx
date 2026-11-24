import { useEffect, useId, useRef, useState } from 'react';
import { heading as toHeading, type PanelId } from '../map/model';
import type { AgvPose } from '../server/agvCommands';
import type { DestinationOption } from './ManualPanel';

export interface PositionDialogProps {
  readonly title: string;
  /** 決めるボタンの文言（「位置リセットする」など）。 */
  readonly confirmLabel: string;
  /** 選べるパネル。 */
  readonly panels: readonly DestinationOption[];
  /** 最初から選んでおく位置（今わかっている位置）。わからなければ null。 */
  readonly initial: AgvPose | null;
  readonly onConfirm: (position: AgvPose) => void;
  readonly onCancel: () => void;
}

const CARDINALS = [
  [0, '北'],
  [90, '東'],
  [180, '南'],
  [270, '西'],
] as const;

const buttonBase =
  'min-h-11 rounded-lg px-4.5 text-[15px] font-bold focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-focus disabled:cursor-not-allowed disabled:opacity-50';
const selectClass = 'min-h-11 rounded-lg border border-control-border bg-white px-2.5 text-[15px] text-graphite focus-visible:outline-3 focus-visible:outline-focus';

/**
 * AGV の位置（パネルと向き）を選んで決める、確かめる画面。位置リセットと、切り離しから戻すときに出す。
 * AGV は十字の上で、ラインの向きにいるので、向きは 0・90・180・270 度から選ぶ。
 */
export function PositionDialog({ title, confirmLabel, panels, initial, onConfirm, onCancel }: PositionDialogProps) {
  const titleId = useId();
  const bodyId = useId();
  const cancelRef = useRef<HTMLButtonElement>(null);
  const [panel, setPanel] = useState<string>(initial !== null && panels.some((p) => p.id === initial.panel) ? initial.panel : '');
  // 向きは、今わかっている向きが 0・90・180・270 度のどれかのときだけ選んでおく。斜めのときやわからないときは未選択（docs/screens.md）
  const [direction, setDirection] = useState<string>(initial !== null && initial.heading % 90 === 0 ? String(initial.heading) : '');

  useEffect(() => {
    cancelRef.current?.focus();
  }, []);

  const chosen = panels.find((p) => p.id === panel);

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-graphite/55 p-4">
      <div
        role="alertdialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={bodyId}
        onKeyDown={(e) => {
          if (e.key === 'Escape') {
            onCancel();
          }
        }}
        className="flex w-full max-w-[440px] flex-col gap-3 rounded-xl bg-paper p-6 text-graphite"
      >
        <h2 id={titleId} className="m-0 text-xl font-bold">
          {title}
        </h2>
        <p id={bodyId} className="m-0 text-[15px] leading-relaxed">
          AGV を手で十字の上まで動かしてから、いるパネルと向きを選んでください。ほかの AGV が予約しているパネルは選べません。
        </p>
        <div className="grid grid-cols-[max-content_1fr] items-center gap-x-3 gap-y-2">
          <label className="text-[13px] font-bold" htmlFor={`${titleId}-panel`}>
            パネル
          </label>
          <select id={`${titleId}-panel`} aria-label="パネル" value={panel} onChange={(e) => setPanel(e.target.value)} className={selectClass}>
            <option value="">選んでください</option>
            {panels.map((p) => (
              <option key={p.id} value={p.id}>
                {p.label}
              </option>
            ))}
          </select>
          <label className="text-[13px] font-bold" htmlFor={`${titleId}-heading`}>
            向き
          </label>
          <select id={`${titleId}-heading`} aria-label="向き" value={direction} onChange={(e) => setDirection(e.target.value)} className={selectClass}>
            <option value="">選んでください</option>
            {CARDINALS.map(([deg, dir]) => (
              <option key={deg} value={deg}>
                {deg}°（{dir}）
              </option>
            ))}
          </select>
        </div>
        <div className="mt-2 flex flex-wrap justify-end gap-2">
          <button ref={cancelRef} type="button" onClick={onCancel} className={`${buttonBase} border border-control-border bg-white text-graphite`}>
            やめる
          </button>
          <button
            type="button"
            disabled={chosen === undefined || direction === ''}
            onClick={() => {
              if (chosen !== undefined && direction !== '') {
                onConfirm({ panel: chosen.id as PanelId, heading: toHeading(Number(direction)) });
              }
            }}
            className={`${buttonBase} bg-graphite text-white`}
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
