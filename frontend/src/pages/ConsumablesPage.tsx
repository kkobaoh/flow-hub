import { useEffect, useId, useRef, useState } from 'react';
import { ConfirmDialog } from '../shared/components/ConfirmDialog';
import {
  cancelNominationBlockedReason,
  draftOfRecord,
  editBlockedReason,
  findConsumables,
  locationKey,
  locationLabel,
  locationOptions,
  nominateBlockedReason,
  recordProblem,
  type ConsumableLocation,
  type ConsumableRecord,
  type RecordDraft,
  type RecordMode,
} from '../features/tool/consumables';
import type { DeviceInside } from '../features/tool/devices';
import { byIdNumber } from '../shared/utils/listing';
import type { Permissions } from '../features/auth/permissions';
import type { Condition } from '../features/tool/station';

/** 登録・直すときに送る記録。 */
export interface RecordInput {
  readonly id: string;
  readonly variety: string;
  readonly condition: Condition;
  readonly location: ConsumableLocation;
}

export interface ConsumablesPageProps {
  readonly records: readonly ConsumableRecord[];
  /** URL で指定した消耗品の ID（開いているもの）。ないときは null。 */
  readonly selected: string | null;
  readonly stations: readonly string[];
  readonly devices: readonly DeviceInside[];
  readonly varieties: readonly string[];
  readonly permissions: Permissions;
  /** サーバーとつながっていない（指名と取り消しを送れない）。 */
  readonly offline: boolean;
  readonly onOpen: (id: string) => void;
  /** 指名する。受け付けたら null、断られたら理由の文を返す。 */
  readonly onNominate: (id: string, device: string) => Promise<string | null>;
  /** 指名を取り消す。受け付けたら null、断られたら理由の文を返す。 */
  readonly onCancelNomination: (id: string) => Promise<string | null>;
  /** 在庫の手入力。受け付けたら null、断られたら理由の文を返す。 */
  readonly onRegister: (record: RecordInput) => Promise<string | null>;
  readonly onEdit: (record: RecordInput) => Promise<string | null>;
  readonly onDelete: (id: string) => Promise<string | null>;
}

const section = 'flex flex-col gap-4 rounded-[10px] border border-line bg-paper p-5';
const button =
  'min-h-11 rounded-lg px-4 text-sm font-bold focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-focus disabled:cursor-not-allowed';
const plainButton = `${button} border border-control-border bg-white text-graphite disabled:border-disabled-border disabled:bg-ground disabled:text-disabled-text`;
const darkButton = `${button} bg-graphite text-white disabled:bg-disabled-border disabled:text-disabled-text`;
const field =
  'min-h-11 rounded-lg border border-control-border bg-white px-2.5 text-[15px] text-graphite focus-visible:outline-3 focus-visible:outline-focus';
const note = 'm-0 text-[13px] text-ink-muted';

const MANUAL_OFFLINE = 'サーバーとつながっていないので、在庫の手入力はできません。';

type Dialog = { readonly kind: 'register' } | { readonly kind: 'edit'; readonly record: ConsumableRecord } | { readonly kind: 'delete'; readonly record: ConsumableRecord };

/** 消耗品の検索と手入力（docs/screens.md の「5 消耗品の検索と手入力」）。1個の消耗品の所在と動きを見て、指名し、記録を直す。 */
export function ConsumablesPage(props: ConsumablesPageProps) {
  const { records, selected, permissions } = props;
  const [query, setQuery] = useState(selected ?? '');
  const [results, setResults] = useState<{ readonly query: string; readonly hits: readonly ConsumableRecord[] } | null>(null);
  const [dialog, setDialog] = useState<Dialog | null>(null);
  const opened = records.find((r) => r.id === selected) ?? null;
  const registerBlocked = permissions.has('stockEdit') ? (props.offline ? MANUAL_OFFLINE : null) : '在庫の手入力は、保守以上のロールで使えます。';
  // 在庫の手入力は、送って結果を待つあいだは押せなくする。断られたら、理由を出す（次に送ったら消す）
  const [sending, setSending] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const send = async (operation: () => Promise<string | null>) => {
    setSending(true);
    setNotice(null);
    try {
      setNotice(await operation());
    } finally {
      setSending(false);
    }
  };

  const submit = () => {
    const q = query.trim();
    const hits = findConsumables(records, q);
    const exact = hits.find((r) => r.id.toUpperCase() === q.toUpperCase());
    if (exact) {
      setResults(null);
      props.onOpen(exact.id);
      return;
    }
    setResults({ query: q, hits });
  };

  return (
    <>
      <div className="flex flex-wrap items-end justify-between gap-3">
        <form
          role="search"
          onSubmit={(e) => {
            e.preventDefault();
            submit();
          }}
          className="flex flex-wrap items-end gap-2"
        >
          <label className="flex flex-col gap-1.5 text-sm font-bold">
            消耗品の ID
            <input type="search" value={query} placeholder="消耗品の ID を入れる" onChange={(e) => setQuery(e.target.value)} className={`${field} w-64 font-normal`} />
          </label>
          <button type="submit" className={darkButton}>
            探す
          </button>
        </form>
        <div className="flex flex-col items-end gap-1">
          <button type="button" disabled={registerBlocked !== null || sending} onClick={() => setDialog({ kind: 'register' })} className={plainButton}>
            手入力で登録する
          </button>
          {registerBlocked !== null && <p className={note}>{registerBlocked}</p>}
        </div>
      </div>

      {notice !== null && (
        <p role="alert" data-manual-notice className="m-0 text-sm font-bold text-attention">
          {notice}
        </p>
      )}

      {results !== null &&
        (results.hits.length === 0 ? (
          <p className="m-0 text-[15px] text-ink-muted">「{results.query}」に合う消耗品はありません。ID を確かめてください。</p>
        ) : (
          <ul aria-label="見つかった消耗品" className="m-0 flex list-none flex-col gap-1.5 p-0">
            {results.hits.map((r) => (
              <li key={r.id}>
                <button
                  type="button"
                  onClick={() => {
                    setResults(null);
                    setQuery(r.id);
                    props.onOpen(r.id);
                  }}
                  className="grid min-h-12 w-full grid-cols-[auto_minmax(0,1fr)] items-center gap-x-3 rounded-lg border border-line bg-white px-3 py-2 text-left hover:border-selection hover:ring-2 hover:ring-selection focus-visible:border-selection focus-visible:ring-2 focus-visible:ring-selection focus-visible:outline-none"
                >
                  <span className="font-number text-[17px] font-bold">{r.id}</span>
                  <span className="text-[13px] text-ink-muted">
                    {r.variety ?? '品種不明'}（{locationLabel(r.location)}）
                  </span>
                </button>
              </li>
            ))}
          </ul>
        ))}

      {opened === null ? (
        results === null && (
          <p className="m-0 rounded-[10px] border border-line bg-paper p-5 text-[15px] text-ink-muted">
            {selected === null ? '消耗品の ID を入れて探すと、所在とこれまでの動きを出します。' : `${selected} は、記録にありません。`}
          </p>
        )
      ) : (
        <>
          <Detail
            key={opened.id}
            record={opened}
            {...props}
            manualSending={sending}
            onEditRecord={() => setDialog({ kind: 'edit', record: opened })}
            onDeleteRecord={() => setDialog({ kind: 'delete', record: opened })}
          />
          <History />
        </>
      )}

      {dialog?.kind === 'delete' && (
        <ConfirmDialog
          title={`${dialog.record.id} の記録を消しますか？`}
          body="記録から消します。付いている予約や指名も解きます。消した記録は、元に戻せません。"
          confirmLabel="記録を消す"
          tone="danger"
          onConfirm={() => {
            const id = dialog.record.id;
            setDialog(null);
            void send(() => props.onDelete(id));
          }}
          onCancel={() => setDialog(null)}
        />
      )}
      {(dialog?.kind === 'register' || dialog?.kind === 'edit') && (
        <RecordDialog
          record={dialog.kind === 'edit' ? dialog.record : null}
          existingIds={records.map((r) => r.id)}
          locations={locationOptions(props.stations, props.devices, dialog.kind === 'edit' ? dialog.record.location : null)}
          varieties={props.varieties}
          onSave={(input) => {
            const save = dialog.kind === 'edit' ? props.onEdit : props.onRegister;
            setDialog(null);
            void send(() => save(input));
          }}
          onCancel={() => setDialog(null)}
        />
      )}
    </>
  );
}

function Detail({
  record: r,
  devices,
  permissions,
  offline,
  manualSending,
  onNominate,
  onCancelNomination,
  onEditRecord,
  onDeleteRecord,
}: ConsumablesPageProps & {
  readonly record: ConsumableRecord;
  /** 在庫の手入力を送って、結果を待っている。 */
  readonly manualSending: boolean;
  readonly onEditRecord: () => void;
  readonly onDeleteRecord: () => void;
}) {
  const [device, setDevice] = useState('');
  // 送って結果を待っているあいだは、指名と取り消しを押せなくする。断られたら、理由を出す（次に送ったら消す）
  const [sending, setSending] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const offlineNote = offline ? 'サーバーとつながっていないので、指名も取り消しもできません。' : null;
  const nominateBlocked = nominateBlockedReason(r, permissions) ?? offlineNote;
  const cancelBlocked = cancelNominationBlockedReason(r, permissions) ?? offlineNote;
  const editBlocked = editBlockedReason(r, permissions) ?? (offline ? MANUAL_OFFLINE : null);
  const send = async (operation: () => Promise<string | null>) => {
    setSending(true);
    setNotice(null);
    try {
      setNotice(await operation());
    } finally {
      setSending(false);
    }
  };
  const reservation =
    r.reservation === null ? 'なし' : r.reservation.transport === null ? r.reservation.purpose : `搬送 #${r.reservation.transport}（${r.reservation.purpose}）`;
  const facts: readonly (readonly [string, string])[] = [
    ['所在', locationLabel(r.location)],
    ['加工の種類', r.processKind ?? '不明'],
    ['予約', reservation],
    ['指名', r.nomination === null ? 'なし' : `${r.nomination.device} へ届けるものに指名`],
  ];

  return (
    <section aria-labelledby="item-title" className={section}>
      <div className="flex flex-wrap items-baseline gap-x-4 gap-y-1">
        <h2 id="item-title" className="m-0 font-number text-[28px] leading-tight font-bold">
          {r.id}
        </h2>
        <span className="text-[15px] font-bold">
          {r.variety ?? '品種不明'}・{r.condition ?? '新品か中古品か不明'}
        </span>
        {r.needsCheck && <span className="rounded-full bg-attention px-2.5 py-0.5 text-[13px] font-bold text-white">要確認</span>}
        {r.shipping && <span className="rounded-full bg-track px-2.5 py-0.5 text-[13px] font-bold">出庫を頼んだ</span>}
      </div>
      <div className="flex flex-wrap gap-6">
        <dl className="m-0 grid flex-[1_1_320px] grid-cols-[auto_minmax(0,1fr)] content-start gap-x-5 gap-y-2.5 text-[15px]">
          {facts.map(([k, v]) => (
            <div key={k} className="contents">
              <dt className="font-bold text-ink-muted">{k}</dt>
              <dd className="m-0">{v}</dd>
            </div>
          ))}
        </dl>
        <div className="flex flex-[1_1_320px] flex-col gap-3">
          <h3 className="m-0 text-sm font-bold text-ink-muted">装置へ届けるものに指名する（AGV での交換）</h3>
          {r.nomination === null ? (
            <>
              <div className="flex flex-wrap items-center gap-2">
                <select aria-label="届ける装置" value={device} disabled={nominateBlocked !== null} onChange={(e) => setDevice(e.target.value)} className={`${field} disabled:bg-ground`}>
                  <option value="">装置を選ぶ</option>
                  {[...devices]
                    .sort((a, b) => byIdNumber(a.id, b.id))
                    .map((d) => (
                      <option key={d.id} value={d.id}>
                        {d.id}
                      </option>
                    ))}
                </select>
                <button type="button" disabled={nominateBlocked !== null || device === '' || sending} onClick={() => void send(() => onNominate(r.id, device))} className={darkButton}>
                  指名する
                </button>
              </div>
              <p className={note}>{nominateBlocked ?? '照合が合わなかった消耗品を回収したあとや、その品種が足りなくなったときに、ほかのものより先に届けます。'}</p>
            </>
          ) : (
            <>
              <div>
                <button type="button" disabled={cancelBlocked !== null || sending} onClick={() => void send(() => onCancelNomination(r.id))} className={plainButton}>
                  指名を取り消す
                </button>
              </div>
              {cancelBlocked !== null && <p className={note}>{cancelBlocked}</p>}
            </>
          )}
          {notice !== null && (
            <p role="alert" className="m-0 text-sm font-bold text-attention">
              {notice}
            </p>
          )}
          <h3 className="m-0 mt-1 text-sm font-bold text-ink-muted">記録</h3>
          <div className="flex flex-wrap gap-2">
            <button type="button" disabled={editBlocked !== null || manualSending} onClick={onEditRecord} className={plainButton}>
              記録を直す
            </button>
            <button type="button" disabled={editBlocked !== null || manualSending} onClick={onDeleteRecord} className={plainButton}>
              記録を消す
            </button>
          </div>
          {editBlocked !== null && <p className={note}>{editBlocked}</p>}
        </div>
      </div>
    </section>
  );
}

/** これまでの動き。記録のしかた（未決事項 #13）を決めるまで、まだ記録していないと出す（backlog）。 */
function History() {
  return (
    <section aria-labelledby="history-title" className={section}>
      <h2 id="history-title" className="m-0 text-lg font-bold">
        これまでの動き
      </h2>
      <p className="m-0 text-[15px] text-ink-muted">これまでの動きは、まだ記録していません（どこまで残すかを決めてから作ります）。</p>
    </section>
  );
}

function RecordDialog({
  record,
  existingIds,
  locations,
  varieties,
  onSave,
  onCancel,
}: {
  readonly record: ConsumableRecord | null;
  readonly existingIds: readonly string[];
  readonly locations: readonly ConsumableLocation[];
  readonly varieties: readonly string[];
  readonly onSave: (input: RecordInput) => void;
  readonly onCancel: () => void;
}) {
  const titleId = useId();
  const dialogRef = useRef<HTMLDivElement>(null);
  const [original] = useState<RecordDraft>(() => (record === null ? { id: '', variety: '', condition: null, location: '' } : draftOfRecord(record)));
  const [draft, setDraft] = useState<RecordDraft>(original);
  const mode: RecordMode = record === null ? { mode: 'register', existingIds } : { mode: 'edit', original };
  const problem = recordProblem(draft, mode);
  const chosen = locations.find((l) => locationKey(l) === draft.location);

  useEffect(() => {
    dialogRef.current?.querySelector<HTMLElement>('input, select')?.focus();
  }, []);

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-graphite/55 p-4">
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        onKeyDown={(e) => {
          if (e.key === 'Escape') {
            onCancel();
          }
        }}
        className="flex max-h-full w-full max-w-[520px] flex-col gap-4 overflow-y-auto rounded-xl bg-paper p-6 text-graphite"
      >
        <h2 id={titleId} className="m-0 text-xl font-bold">
          {record === null ? '手入力で登録する' : `${record.id} の記録を直す`}
        </h2>
        <p className={note}>現物と合わない記録を直します。所在は、ステーションか、装置のストッカーの空いているところから選びます。</p>
        <div className="flex flex-col gap-3">
          {record === null && (
            <label className="flex flex-col gap-1.5 text-sm font-bold">
              ID
              <input
                aria-label="ID"
                value={draft.id}
                onChange={(e) => setDraft((d) => ({ ...d, id: e.target.value }))}
                className={`${field} font-number text-[17px] font-semibold`}
              />
            </label>
          )}
          <label className="flex flex-col gap-1.5 text-sm font-bold">
            品種
            <select
              aria-label="品種"
              value={draft.variety}
              onChange={(e) => setDraft((d) => ({ ...d, variety: e.target.value }))}
              className={`${field} font-normal`}
            >
              <option value="">選ぶ</option>
              {varieties.map((v) => (
                <option key={v} value={v}>
                  {v}
                </option>
              ))}
            </select>
          </label>
          <fieldset className="m-0 flex flex-col gap-1.5 border-0 p-0">
            <legend className="p-0 text-sm font-bold">新品・中古品</legend>
            <div className="flex gap-4">
              {(['新品', '中古品'] as const).map((c) => (
                <label key={c} className="flex min-h-11 items-center gap-1.5 text-[15px]">
                  <input
                    type="radio"
                    name={`${titleId}-condition`}
                    value={c}
                    checked={draft.condition === c}
                    onChange={() => setDraft((d) => ({ ...d, condition: c }))}
                    className="size-5 accent-graphite"
                  />
                  {c}
                </label>
              ))}
            </div>
          </fieldset>
          <label className="flex flex-col gap-1.5 text-sm font-bold">
            所在
            <select aria-label="所在" value={draft.location} onChange={(e) => setDraft((d) => ({ ...d, location: e.target.value }))} className={`${field} font-normal`}>
              <option value="">選ぶ</option>
              {locations.map((l) => (
                <option key={locationKey(l)} value={locationKey(l)}>
                  {locationLabel(l)}
                </option>
              ))}
            </select>
          </label>
        </div>
        {problem !== null && <p className="m-0 text-sm text-ink-muted">{problem}</p>}
        <div className="flex flex-wrap justify-end gap-2">
          <button type="button" onClick={onCancel} className={plainButton}>
            やめる
          </button>
          <button
            type="button"
            disabled={problem !== null || chosen === undefined || draft.condition === null}
            onClick={() => {
              if (chosen !== undefined && draft.condition !== null) {
                onSave({ id: draft.id.trim(), variety: draft.variety, condition: draft.condition, location: chosen });
              }
            }}
            className={darkButton}
          >
            記録を保存する
          </button>
        </div>
      </div>
    </div>
  );
}
