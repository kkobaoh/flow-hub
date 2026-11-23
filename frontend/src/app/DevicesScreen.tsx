import { useState, type ReactNode } from 'react';
import type { EquipmentId } from '../map/equipment';
import { byIdNumber } from './listing';
import {
  chooseDevice,
  draftOf,
  settingProblem,
  slotCapacity,
  toSaved,
  type DeviceInside,
  type DraftRow,
  type HeldConsumable,
  type SavedRow,
  type SettingRow,
  type SlotContent,
} from './devices';
import type { Permissions } from './permissions';

export interface DevicesScreenProps {
  readonly devices: readonly DeviceInside[];
  /** URL で指定した装置（マップから来たときなど）。ないときは null。 */
  readonly selected: string | null;
  /** 登録してある品種（設定に加えられるもの）。 */
  readonly varieties: readonly string[];
  readonly permissions: Permissions;
  readonly onSelect: (id: EquipmentId) => void;
  readonly onSaveSetting: (id: EquipmentId, setting: readonly SavedRow[]) => void;
}

const section = 'flex flex-col gap-4 rounded-[10px] border border-line bg-paper p-5';
const button =
  'min-h-11 rounded-lg px-4 text-sm font-bold focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-focus disabled:cursor-not-allowed';
const plainButton = `${button} border border-control-border bg-white text-graphite disabled:border-disabled-border disabled:bg-ground disabled:text-disabled-text`;
const darkButton = `${button} bg-graphite text-white disabled:bg-disabled-border disabled:text-disabled-text`;
const th = 'border-b border-line px-3 py-2 text-left font-bold text-ink-muted';

/** 装置とストッカー（docs/screens.md の「3 装置とストッカー」）。装置の中の様子と、ストッカーの設定（段取り替え）。 */
export function DevicesScreen({ devices, selected, varieties, permissions, onSelect, onSaveSetting }: DevicesScreenProps) {
  const device = chooseDevice(devices, selected);
  if (device === null) {
    return <p className="m-0 rounded-[10px] border border-line bg-paper p-5 text-[15px] text-ink-muted">登録してある装置はありません。装置は、消耗品の構成の画面で加えます。</p>;
  }
  const ordered = [...devices].sort((a, b) => byIdNumber(a.id, b.id));

  return (
    <>
      <label className="flex items-center gap-2 self-start text-sm font-bold">
        装置
        <select
          aria-label="装置"
          value={device.id}
          onChange={(e) => onSelect(e.target.value as EquipmentId)}
          className="min-h-11 min-w-[220px] rounded-lg border border-control-border bg-white px-2.5 text-[15px] font-normal text-graphite focus-visible:outline-3 focus-visible:outline-focus"
        >
          {ordered.map((d) => (
            <option key={d.id} value={d.id}>
              {d.id}（受け渡しパネル {d.panel}）
            </option>
          ))}
        </select>
      </label>
      <Inside device={device} />
      <SettingSection key={device.id} device={device} varieties={varieties} permissions={permissions} onSave={(rows) => onSaveSetting(device.id, rows)} />
    </>
  );
}

function Inside({ device }: { readonly device: DeviceInside }) {
  return (
    <section aria-labelledby="inside-title" className={section}>
      <h2 id="inside-title" className="m-0 text-lg font-bold">
        装置の中
      </h2>
      <div className="flex flex-wrap items-stretch gap-4">
        <Part title="加工部" grow="flex-[1_1_180px]">
          <Box item={device.processing} dashed />
        </Part>
        {device.stockers.map((s) => (
          <Part key={s.name} title={s.name} grow="flex-[2_1_320px]">
            {s.layout === 'slots' ? (
              <div className="grid grid-cols-[repeat(auto-fill,minmax(104px,1fr))] gap-2">
                {s.slots.map((slot) => (
                  <SlotBox key={slot.number} number={slot.number} content={slot.content} />
                ))}
              </div>
            ) : (
              <div className="flex flex-col gap-2">
                <p className="m-0 text-[13px] text-ink-muted">プレートに載せたまま置くストッカー（スロットはありません）</p>
                {s.items.length === 0 ? <Box item={null} /> : s.items.map((item, i) => <Box key={item.id ?? i} item={item} />)}
              </div>
            )}
          </Part>
        ))}
        <Part title="受け渡し部" grow="flex-[1_1_160px]">
          <Box item={device.handover} />
        </Part>
      </div>
    </section>
  );
}

function Part({ title, grow, children }: { readonly title: string; readonly grow: string; readonly children: ReactNode }) {
  return (
    <div className={`flex flex-col gap-2 ${grow}`}>
      <h3 className="m-0 text-sm font-bold text-ink-muted">{title}</h3>
      {children}
    </div>
  );
}

const noteTone: Record<HeldConsumable['tone'], string> = {
  normal: 'text-ink-muted',
  collect: 'text-attention',
  blocked: 'text-attention',
};

function Consumable({ item }: { readonly item: HeldConsumable }) {
  return (
    <>
      <div className="font-bold">{item.variety}</div>
      {item.id !== null && <div className="font-number text-base font-semibold">{item.id}</div>}
      <div className={`mt-auto text-xs font-bold ${noteTone[item.tone]}`}>{item.note}</div>
    </>
  );
}

function Box({ item, dashed = false }: { readonly item: HeldConsumable | null; readonly dashed?: boolean }) {
  if (item === null) {
    return <div className="flex min-h-[120px] flex-1 items-center justify-center rounded-lg border border-line bg-ground text-sm text-ink-muted">空き</div>;
  }
  const border = item.tone === 'blocked' ? 'border-2 border-attention' : dashed ? 'border-2 border-dashed border-control-border' : 'border border-control-border';
  return <div className={`flex min-h-[120px] flex-1 flex-col gap-0.5 rounded-lg bg-white p-3 ${border}`}><Consumable item={item} /></div>;
}

function SlotBox({ number, content }: { readonly number: number; readonly content: SlotContent }) {
  const look =
    content.kind === 'held'
      ? content.consumable.tone === 'blocked'
        ? 'border-2 border-attention bg-white'
        : 'border border-control-border bg-white'
      : 'border border-dashed border-control-border bg-ground';
  return (
    <div className={`flex min-h-[120px] flex-col gap-0.5 rounded-lg p-2.5 ${look}`}>
      <div className="text-xs text-ink-muted">スロット{number}</div>
      {content.kind === 'empty' ? <div className="mt-auto text-xs text-ink-muted">空き</div> : <Consumable item={content.consumable} />}
    </div>
  );
}

const statusLabel: Record<SettingRow['status'], string> = {
  enough: '足りている',
  short: '足りない（届ける）',
  excess: '設定より多い',
  notInSetting: '設定にない（回収する）',
};

const statusTone: Record<SettingRow['status'], string> = {
  enough: 'text-running',
  short: 'text-attention',
  excess: 'text-graphite',
  notInSetting: 'text-attention',
};

function SettingSection({
  device,
  varieties,
  permissions,
  onSave,
}: {
  readonly device: DeviceInside;
  readonly varieties: readonly string[];
  readonly permissions: Permissions;
  readonly onSave: (rows: readonly SavedRow[]) => void;
}) {
  const [draft, setDraft] = useState<DraftRow[] | null>(null);
  const [adding, setAdding] = useState('');
  const allowed = permissions.has('stockerSetting');
  const capacity = slotCapacity(device);
  const problem = draft === null ? null : settingProblem(draft, capacity);
  const addable = draft === null ? [] : varieties.filter((v) => !draft.some((r) => r.variety === v));
  const current = (variety: string) => device.setting.find((r) => r.variety === variety);

  const edit = (variety: string, target: string) => setDraft((d) => d?.map((r) => (r.variety === variety ? { ...r, target } : r)) ?? null);

  return (
    <section aria-labelledby="setting-title" className={section}>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 id="setting-title" className="m-0 text-lg font-bold">
          ストッカーの設定
        </h2>
        {draft === null && (
          <button type="button" disabled={!allowed} onClick={() => setDraft(draftOf(device.setting))} className={plainButton}>
            設定を変える（段取り替え）
          </button>
        )}
      </div>
      <p className="m-0 text-sm text-ink-muted">
        どの品種を何個置いておくかを決めます。設定を変えると、足りないものを届け、設定にないものを回収します。
        {capacity === null ? '' : `スロットは ${capacity} 個あります。`}
      </p>
      {!allowed && <p className="m-0 text-sm font-bold">ストッカーの設定は、運用以上のロールで使えます。</p>}

      <div className="overflow-x-auto">
        <table className="w-full min-w-[600px] border-collapse text-sm">
          <thead>
            <tr>
              <th scope="col" className={th}>
                品種
              </th>
              <th scope="col" className={th}>
                設定の個数
              </th>
              <th scope="col" className={th}>
                ある新品（届ける途中を含む）
              </th>
              <th scope="col" className={th}>
                状態
              </th>
              {draft !== null && (
                <th scope="col" className={th}>
                  <span className="sr-only">外す</span>
                </th>
              )}
            </tr>
          </thead>
          <tbody>
            {draft === null
              ? device.setting.map((r) => (
                  <tr key={r.variety} className="border-b border-disabled-border">
                    <td className="px-3 py-2.5 font-bold">{r.variety}</td>
                    <td className="px-3 py-2.5 font-number text-[17px] font-semibold">{r.status === 'notInSetting' ? '－' : r.target}</td>
                    <td className="px-3 py-2.5 font-number text-[17px] font-semibold">{r.have}</td>
                    <td className={`px-3 py-2.5 font-bold ${statusTone[r.status]}`}>{statusLabel[r.status]}</td>
                  </tr>
                ))
              : draft.map((r) => (
                  <tr key={r.variety} className="border-b border-disabled-border">
                    <td className="px-3 py-2 font-bold">{r.variety}</td>
                    <td className="px-3 py-2">
                      <input
                        type="number"
                        min={0}
                        step={1}
                        inputMode="numeric"
                        value={r.target}
                        aria-label={`${r.variety} の設定の個数`}
                        onChange={(e) => edit(r.variety, e.target.value)}
                        className="min-h-10 w-22 rounded-md border border-control-border bg-white px-2 font-number text-[17px] font-semibold focus-visible:outline-3 focus-visible:outline-focus"
                      />
                    </td>
                    <td className="px-3 py-2 font-number text-[17px] font-semibold">{current(r.variety)?.have ?? 0}</td>
                    <td className="px-3 py-2 text-ink-muted">{current(r.variety) ? statusLabel[current(r.variety)!.status] : '加える品種'}</td>
                    <td className="px-3 py-2 text-right">
                      <button
                        type="button"
                        aria-label={`${r.variety} を設定から外す`}
                        onClick={() => setDraft((d) => d?.filter((x) => x.variety !== r.variety) ?? null)}
                        className={plainButton}
                      >
                        外す
                      </button>
                    </td>
                  </tr>
                ))}
          </tbody>
        </table>
        {draft === null && device.setting.length === 0 && <p className="m-0 py-3 text-sm text-ink-muted">まだ設定していません。設定するまでは、届けることも回収することもしません。</p>}
      </div>

      {draft !== null && (
        <div className="flex flex-col gap-3">
          {problem !== null && (
            <p role="alert" className="m-0 text-sm font-bold text-attention">
              {problem}
            </p>
          )}
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div className="flex flex-wrap items-center gap-2">
              <select
                aria-label="加える品種"
                value={adding}
                onChange={(e) => setAdding(e.target.value)}
                disabled={addable.length === 0}
                className="min-h-11 rounded-lg border border-control-border bg-white px-2.5 text-[15px] text-graphite focus-visible:outline-3 focus-visible:outline-focus disabled:bg-ground"
              >
                <option value="">品種を選ぶ</option>
                {addable.map((v) => (
                  <option key={v} value={v}>
                    {v}
                  </option>
                ))}
              </select>
              <button
                type="button"
                disabled={adding === ''}
                onClick={() => {
                  setDraft((d) => (d === null ? d : [...d, { variety: adding, target: '' }]));
                  setAdding('');
                }}
                className={plainButton}
              >
                加える
              </button>
            </div>
            <div className="flex gap-2">
              <button type="button" onClick={() => setDraft(null)} className={plainButton}>
                やめる
              </button>
              <button
                type="button"
                disabled={problem !== null}
                onClick={() => {
                  onSave(toSaved(draft));
                  setDraft(null);
                }}
                className={darkButton}
              >
                設定を保存する
              </button>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}
