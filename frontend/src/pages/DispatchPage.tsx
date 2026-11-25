import { useState } from 'react';
import { clockOf } from '../shared/utils/dateTime';
import { ConfirmDialog } from '../shared/components/ConfirmDialog';
import type { Permissions } from '../features/auth/permissions';
import {
  cancelBlockedReason,
  initialTransportTab,
  permissionNote,
  retryBlockedReason,
  transportsOf,
  type Transport,
  type TransportStage,
} from '../features/dispatch/transports';

export interface DispatchPageProps {
  readonly transports: readonly Transport[];
  readonly permissions: Permissions;
  /** サーバーとつながっていない（操作を送れない）。 */
  readonly offline: boolean;
  /** 搬送を取りやめる（確かめる画面で選んだあと）。結果の知らせがあれば、その文を返す。 */
  readonly onCancel: (number: number) => Promise<string | null>;
}

const TABS: readonly { readonly stage: TransportStage; readonly label: string; readonly note: string; readonly empty: string }[] = [
  {
    stage: 'queued',
    label: '待ち行列',
    note: '必要時刻が早い順に並べています。割り当てられる AGV が空いたら、上から割り当てます。',
    empty: '待ち行列にある搬送はありません。',
  },
  {
    stage: 'running',
    label: '走っている',
    note: '取りやめると、まだ AGV に渡していない指示を取り下げます。AGV は、受け取った指示を終えたところで止まります。',
    empty: '走っている搬送はありません。',
  },
  {
    stage: 'suspended',
    label: '中断した',
    note: 'AGV が異常になったか、経路が見つからずに止まった搬送です。やり直すか取りやめるかは、異常のときの扱いを決めてから作ります。',
    empty: '中断した搬送はありません。',
  },
];

const CANCEL_BODY: Record<TransportStage, string> = {
  queued: 'まだ AGV を割り当てていない搬送の要求を、取り下げます。',
  running: 'まだ AGV に渡していない指示を取り下げます。AGV は、受け取った指示を終えたところで止まります。ものを積んでいれば、載せたまま待ちます。',
  suspended: '中断した搬送を終わりにします。AGV がものを積んでいれば、載せたまま待ちます。',
};

const actionButton =
  'min-h-11 rounded-lg px-3.5 text-sm font-bold whitespace-nowrap focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-focus disabled:cursor-not-allowed';
const cell = 'px-3 py-3.5 align-top';

/** 搬送の一覧（docs/screens.md の「2 搬送の一覧」）。待ち行列、走っている搬送、中断した搬送をタブで分けて見る。 */
export function DispatchPage({ transports, permissions, offline, onCancel }: DispatchPageProps) {
  const [stage, setStage] = useState<TransportStage>(() => initialTransportTab(transports));
  const [pending, setPending] = useState<Transport | null>(null);
  // 送って結果を待っているあいだは、取りやめを押せなくする。結果の知らせは、一覧の上に出す（次に送ったら消す）
  const [sending, setSending] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const current = TABS.find((t) => t.stage === stage) ?? TABS[1]!;
  const rows = transportsOf(transports, stage);
  const noPermission = permissionNote(permissions);

  const confirm = async () => {
    if (pending === null) {
      return;
    }
    const number = pending.number;
    setPending(null);
    setSending(true);
    setNotice(null);
    try {
      setNotice(await onCancel(number));
    } finally {
      setSending(false);
    }
  };

  return (
    <>
      <div role="tablist" aria-label="搬送の段階" className="flex flex-wrap gap-1 border-b-2 border-line">
        {TABS.map((t) => {
          const count = transportsOf(transports, t.stage).length;
          const selected = t.stage === stage;
          const alert = t.stage === 'suspended' && count > 0;
          return (
            <button
              key={t.stage}
              type="button"
              role="tab"
              aria-selected={selected}
              onClick={() => setStage(t.stage)}
              className={`-mb-0.5 flex min-h-12 items-center gap-2 border-b-3 px-4.5 text-[15px] font-bold focus-visible:outline-3 focus-visible:outline-focus ${selected ? 'border-graphite text-graphite' : 'border-transparent text-ink-muted'}`}
            >
              {t.label}
              <span
                className={`inline-flex h-6 min-w-6 items-center justify-center rounded-full px-1.5 font-number text-[15px] ${alert ? 'bg-attention text-white' : 'bg-track text-graphite'}`}
              >
                {count}
              </span>
            </button>
          );
        })}
      </div>
      <p className="m-0 text-sm text-ink-muted">{current.note}</p>
      {noPermission !== null && <p className="m-0 text-sm font-bold text-graphite">{noPermission}</p>}
      {notice !== null && (
        <p role="status" className="m-0 text-sm font-bold text-attention">
          {notice}
        </p>
      )}

      {rows.length === 0 ? (
        <p className="m-0 rounded-[10px] border border-line bg-paper p-5 text-[15px] text-ink-muted">{current.empty}</p>
      ) : (
        <div className="relative overflow-x-auto rounded-[10px] border border-line bg-paper">
          <table className="w-full min-w-[860px] border-collapse text-sm">
            <thead>
              <tr className="text-left text-ink-muted">
                <th scope="col" className="border-b border-line px-4 py-2.5 font-bold">
                  搬送
                </th>
                <th scope="col" className="border-b border-line px-3 py-2.5 font-bold">
                  種類
                </th>
                <th scope="col" className="border-b border-line px-3 py-2.5 font-bold">
                  搬送元から搬送先
                </th>
                <th scope="col" className="border-b border-line px-3 py-2.5 font-bold">
                  必要時刻
                </th>
                <th scope="col" className="border-b border-line px-3 py-2.5 font-bold">
                  AGV
                </th>
                <th scope="col" className="border-b border-line px-3 py-2.5 font-bold">
                  いまの段階
                </th>
                <th scope="col" className="border-b border-line px-4 py-2.5">
                  <span className="sr-only">操作</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {rows.map((t) => {
                const suspended = t.status.stage === 'suspended';
                const cancelBlocked = cancelBlockedReason(t, permissions, offline);
                // 権限がないことは、一覧の上に1つだけ出す
                const reason = noPermission === null ? (suspended ? retryBlockedReason(permissions) : cancelBlocked) : null;
                return (
                  <tr key={t.number} className="border-b border-disabled-border last:border-b-0">
                    <td className="px-4 py-3.5 align-top font-number text-lg font-bold">#{t.number}</td>
                    <td className={cell}>{t.kind}</td>
                    <td className={cell}>
                      <div className="font-bold">
                        {t.from} から {t.to}
                      </div>
                      <div className="text-[13px] text-ink-muted">{t.cargo}</div>
                    </td>
                    <td className={`${cell} font-number text-base font-semibold`}>{clockOf(t.neededBy)}</td>
                    <td className={cell}>{t.agv ?? '－'}</td>
                    <td className={cell}>
                      <div className="font-bold">{t.step}</div>
                      {t.stepNote !== '' && <div className="text-[13px] text-ink-muted">{t.stepNote}</div>}
                    </td>
                    <td className="px-4 py-2.5 align-top">
                      <div className="flex justify-end gap-2">
                        {suspended && (
                          <button type="button" disabled className={`${actionButton} bg-graphite text-white disabled:bg-disabled-border disabled:text-disabled-text`}>
                            やり直す
                          </button>
                        )}
                        <button
                          type="button"
                          disabled={cancelBlocked !== null || sending}
                          onClick={() => setPending(t)}
                          className={`${actionButton} border border-control-border bg-white text-graphite disabled:border-disabled-border disabled:bg-ground disabled:text-disabled-text`}
                        >
                          取りやめる
                        </button>
                      </div>
                      {reason !== null && <p className="m-0 mt-1.5 ml-auto max-w-60 text-right text-[13px] text-ink-muted">{reason}</p>}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {pending !== null && (
        <ConfirmDialog
          title={`搬送 #${pending.number} を取りやめますか？`}
          body={CANCEL_BODY[pending.status.stage]}
          confirmLabel="取りやめる"
          tone="danger"
          onConfirm={() => void confirm()}
          onCancel={() => setPending(null)}
        />
      )}
    </>
  );
}
