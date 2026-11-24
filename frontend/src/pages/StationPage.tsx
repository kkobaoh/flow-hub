import { useEffect, useId, useRef, useState } from 'react';
import { dateTimeOf } from '../shared/utils/dateTime';
import type { EquipmentId } from '../features/tool/equipment';
import { ConfirmDialog } from '../shared/components/ConfirmDialog';
import { byIdNumber } from '../shared/utils/listing';
import type { Permissions } from '../features/auth/permissions';
import {
  fixBlockedReason,
  fixProblem,
  shipBlockedReason,
  shipNote,
  sortStationItems,
  stateLabel,
  type Condition,
  type Fix,
  type StationInventory,
  type StationItem,
} from '../features/tool/station';

export interface StationPageProps {
  readonly stations: readonly StationInventory[];
  /** URL で指定したステーション（マップから来たときなど）。ないときは null。 */
  readonly selected: string | null;
  readonly varieties: readonly string[];
  readonly permissions: Permissions;
  readonly onSelect: (id: EquipmentId) => void;
  /** 出庫を頼む（確かめる画面で選んだあと）。 */
  readonly onShip: (station: EquipmentId, item: string) => void;
  readonly onFix: (station: EquipmentId, item: string, fix: Fix) => void;
}

const section = 'flex flex-col gap-3 rounded-[10px] border border-line bg-paper p-5';
const th = 'border-b border-line px-3 py-2 text-left font-bold text-ink-muted';
const button =
  'min-h-11 rounded-lg px-3.5 text-sm font-bold whitespace-nowrap focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-focus disabled:cursor-not-allowed';
const plainButton = `${button} border border-control-border bg-white text-graphite disabled:border-disabled-border disabled:bg-ground disabled:text-disabled-text`;
const darkButton = `${button} bg-graphite text-white disabled:bg-disabled-border disabled:text-disabled-text`;

/** 出す ステーション：指定したものがあればそれ、なければ番号がいちばん小さいもの。 */
function chooseStation(stations: readonly StationInventory[], wanted: string | null): StationInventory | null {
  return stations.find((s) => s.id === wanted) ?? [...stations].sort((a, b) => byIdNumber(a.id, b.id))[0] ?? null;
}

/** ステーションの在庫（docs/screens.md の「4 ステーションの在庫」）。品種ごとの数、出庫、要確認の消耗品を直す。 */
export function StationPage({ stations, selected, varieties, permissions, onSelect, onShip, onFix }: StationPageProps) {
  const [checkOnly, setCheckOnly] = useState(false);
  const [shipping, setShipping] = useState<StationItem | null>(null);
  const [fixing, setFixing] = useState<StationItem | null>(null);
  const station = chooseStation(stations, selected);
  if (station === null) {
    return <p className="m-0 rounded-[10px] border border-line bg-paper p-5 text-[15px] text-ink-muted">登録してあるステーションはありません。ステーションは、消耗品の構成の画面で加えます。</p>;
  }
  const items = sortStationItems(station.items);
  const checkCount = items.filter((i) => i.state.kind === 'needsCheck').length;
  const shown = checkOnly ? items.filter((i) => i.state.kind === 'needsCheck') : items;
  const fixBlocked = fixBlockedReason(permissions);

  return (
    <>
      {stations.length > 1 ? (
        <label className="flex items-center gap-2 self-start text-sm font-bold">
          ステーション
          <select
            value={station.id}
            onChange={(e) => onSelect(e.target.value as EquipmentId)}
            className="min-h-11 min-w-[220px] rounded-lg border border-control-border bg-white px-2.5 text-[15px] font-normal focus-visible:outline-3 focus-visible:outline-focus"
          >
            {[...stations]
              .sort((a, b) => byIdNumber(a.id, b.id))
              .map((s) => (
                <option key={s.id} value={s.id}>
                  {s.id}（受け渡しパネル {s.panel}）
                </option>
              ))}
          </select>
        </label>
      ) : (
        <p className="m-0 text-[15px] text-ink-muted">
          {station.id}（受け渡しパネル {station.panel}）
        </p>
      )}

      <section aria-labelledby="summary-title" className={section}>
        <h2 id="summary-title" className="m-0 text-lg font-bold">
          品種ごとの数
        </h2>
        <div className="relative overflow-x-auto">
          <table className="w-full min-w-[560px] border-collapse text-sm">
            <thead>
              <tr>
                <th scope="col" className={th}>
                  品種
                </th>
                <th scope="col" className={th}>
                  届けられる新品
                </th>
                <th scope="col" className={th}>
                  届ける予約あり
                </th>
                <th scope="col" className={th}>
                  補充を頼んで、まだ入っていない
                </th>
              </tr>
            </thead>
            <tbody>
              {station.summary.map((s) => (
                <tr key={s.variety} className="border-b border-disabled-border font-number text-[17px] font-semibold">
                  <td className="px-3 py-2.5 font-sans text-sm font-bold">{s.variety}</td>
                  <td className="px-3 py-2.5">{s.available}</td>
                  <td className="px-3 py-2.5">{s.reserved}</td>
                  <td className="px-3 py-2.5">{s.replenishWaiting}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section aria-labelledby="items-title" className={section}>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 id="items-title" className="m-0 text-lg font-bold">
            ステーションにある消耗品
          </h2>
          <div role="group" aria-label="表示する消耗品" className="flex overflow-hidden rounded-lg border border-control-border bg-white">
            {(
              [
                [false, 'すべて'],
                [true, `要確認だけ（${checkCount}）`],
              ] as const
            ).map(([value, label]) => (
              <button
                key={label}
                type="button"
                aria-pressed={checkOnly === value}
                onClick={() => setCheckOnly(value)}
                className={`min-h-11 px-4 text-sm font-bold focus-visible:outline-3 focus-visible:-outline-offset-3 focus-visible:outline-focus ${checkOnly === value ? 'bg-graphite text-white' : 'bg-white text-graphite'}`}
              >
                {label}
              </button>
            ))}
          </div>
        </div>
        {fixBlocked !== null && <p className="m-0 text-sm font-bold">{fixBlocked}</p>}
        {shown.length === 0 ? (
          <p className="m-0 py-2 text-[15px] text-ink-muted">{checkOnly ? '要確認の消耗品はありません。' : 'ステーションに消耗品はありません。'}</p>
        ) : (
          <div className="relative overflow-x-auto">
            <table className="w-full min-w-[820px] border-collapse text-sm">
              <thead>
                <tr>
                  <th scope="col" className={th}>
                    ID
                  </th>
                  <th scope="col" className={th}>
                    品種
                  </th>
                  <th scope="col" className={th}>
                    新品・中古品
                  </th>
                  <th scope="col" className={th}>
                    入庫した日時
                  </th>
                  <th scope="col" className={th}>
                    状態
                  </th>
                  <th scope="col" className={th}>
                    <span className="sr-only">操作</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {shown.map((i) => {
                  const check = i.state.kind === 'needsCheck';
                  const blocked = shipBlockedReason(i, permissions);
                  return (
                    <tr key={i.id} className={`border-b border-disabled-border align-top ${check ? 'bg-white' : ''}`}>
                      <td className="px-3 py-2.5 font-number text-[17px] font-semibold">{i.id}</td>
                      <td className="px-3 py-2.5 font-bold">{i.variety ?? '不明'}</td>
                      <td className="px-3 py-2.5">{i.condition ?? '不明'}</td>
                      <td className="px-3 py-2.5 font-number text-base font-medium">{dateTimeOf(i.receivedAt)}</td>
                      <td className={`px-3 py-2.5 font-bold ${check ? 'text-attention' : i.state.kind === 'available' ? 'text-running' : 'text-graphite'}`}>{stateLabel(i.state)}</td>
                      <td className="px-3 py-1.5">
                        <div className="flex justify-end gap-2">
                          {check && (
                            <button type="button" disabled={fixBlocked !== null} onClick={() => setFixing(i)} className={darkButton}>
                              確かめて直す
                            </button>
                          )}
                          <button type="button" disabled={blocked !== null} onClick={() => setShipping(i)} className={plainButton}>
                            出庫する
                          </button>
                        </div>
                        {blocked !== null && fixBlocked === null && <p className="m-0 mt-1 ml-auto max-w-60 text-right text-[13px] text-ink-muted">{blocked}</p>}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {shipping !== null && (
        <ConfirmDialog
          title={`${shipping.id} を出庫しますか？`}
          body={shipNote(shipping)}
          confirmLabel="出庫する"
          tone="neutral"
          onConfirm={() => {
            onShip(station.id, shipping.id);
            setShipping(null);
          }}
          onCancel={() => setShipping(null)}
        />
      )}
      {fixing !== null && (
        <FixDialog
          item={fixing}
          varieties={varieties}
          onSave={(fix) => {
            onFix(station.id, fixing.id, fix);
            setFixing(null);
          }}
          onCancel={() => setFixing(null)}
        />
      )}
    </>
  );
}

function FixDialog({ item, varieties, onSave, onCancel }: { readonly item: StationItem; readonly varieties: readonly string[]; readonly onSave: (fix: Fix) => void; readonly onCancel: () => void }) {
  const titleId = useId();
  const varietyId = useId();
  const [fix, setFix] = useState<Fix>({ variety: '', condition: null });
  const firstRef = useRef<HTMLSelectElement>(null);
  const problem = fixProblem(fix);

  useEffect(() => {
    firstRef.current?.focus();
  }, []);

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-graphite/55 p-4">
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        onKeyDown={(e) => {
          if (e.key === 'Escape') {
            onCancel();
          }
        }}
        className="flex w-full max-w-[480px] flex-col gap-4 rounded-xl bg-paper p-6 text-graphite"
      >
        <h2 id={titleId} className="m-0 text-xl font-bold">
          要確認の消耗品を直す：<span className="font-number">{item.id}</span>
        </h2>
        <p className="m-0 text-sm text-ink-muted">
          {item.state.kind === 'needsCheck' ? item.state.reason : ''}現物を確かめて、品種と、新品か中古品かを選んでください。
        </p>
        <div className="flex flex-wrap gap-4">
          <div className="flex flex-col gap-1.5">
            <label htmlFor={varietyId} className="text-sm font-bold">
              品種
            </label>
            <select
              ref={firstRef}
              id={varietyId}
              value={fix.variety}
              onChange={(e) => setFix((f) => ({ ...f, variety: e.target.value }))}
              className="min-h-11 min-w-[200px] rounded-lg border border-control-border bg-white px-2.5 text-[15px] focus-visible:outline-3 focus-visible:outline-focus"
            >
              <option value="">選ぶ</option>
              {varieties.map((v) => (
                <option key={v} value={v}>
                  {v}
                </option>
              ))}
            </select>
          </div>
          <fieldset className="m-0 flex flex-col gap-1.5 border-0 p-0">
            <legend className="p-0 text-sm font-bold">新品・中古品</legend>
            <div className="flex gap-4">
              {(['新品', '中古品'] as const satisfies readonly Condition[]).map((c) => (
                <label key={c} className="flex min-h-11 items-center gap-1.5 text-[15px]">
                  <input
                    type="radio"
                    name={`${titleId}-condition`}
                    value={c}
                    checked={fix.condition === c}
                    onChange={() => setFix((f) => ({ ...f, condition: c }))}
                    className="size-5 accent-graphite"
                  />
                  {c}
                </label>
              ))}
            </div>
          </fieldset>
        </div>
        {problem !== null && <p className="m-0 text-sm text-ink-muted">{problem}</p>}
        <div className="flex flex-wrap justify-end gap-2">
          <button type="button" onClick={onCancel} className={plainButton}>
            やめる
          </button>
          <button type="button" disabled={problem !== null} onClick={() => onSave(fix)} className={darkButton}>
            直して、要確認を外す
          </button>
        </div>
      </div>
    </div>
  );
}
