import { useState, type ReactNode } from 'react';
import {
  carryMethodLabel,
  deviceDeleteBlockedReason,
  deviceDraftOf,
  deviceProblem,
  idProblem,
  panelChoices,
  processKindDeleteBlockedReason,
  processKindProblem,
  removeIdBlockedReason,
  stationDeleteBlockedReason,
  stationProblem,
  toDeviceLayout,
  varietyDeleteBlockedReason,
  varietyProblem,
  type CarryMethod,
  type DeviceConfig,
  type DeviceDraft,
  type DeviceLayout,
  type ProcessKind,
  type ProcessKindDraft,
  type StationConfig,
  type Variety,
} from './config';
import { ConfirmDialog } from './ConfirmDialog';
import { Choice, darkButton, FormDialog, FormFooter, note, plainButton, RowActions, section, td, TextField, th } from './forms';
import { byIdNumber } from './listing';
import type { Permissions } from './permissions';

export interface ConfigScreenProps {
  readonly processKinds: readonly ProcessKind[];
  readonly varieties: readonly Variety[];
  readonly devices: readonly DeviceConfig[];
  readonly stations: readonly StationConfig[];
  readonly carriers: readonly string[];
  /** 公開中のマップの受け渡しパネル（特殊消耗品受け渡しパネルを含む）。 */
  readonly transferPanels: readonly string[];
  readonly permissions: Permissions;
  /** 保存する。original は直す前の名前（加えるときは null）。ほかも同じ。 */
  readonly onSaveProcessKind: (original: string | null, kind: ProcessKind) => void;
  readonly onDeleteProcessKind: (name: string) => void;
  readonly onSaveVariety: (original: string | null, variety: { readonly name: string; readonly processKind: string }) => void;
  readonly onDeleteVariety: (name: string) => void;
  readonly onAddId: (variety: string, id: string) => void;
  readonly onRemoveId: (variety: string, id: string) => void;
  readonly onSaveDevice: (original: string | null, device: { readonly id: string; readonly panel: string; readonly layout: DeviceLayout }) => void;
  readonly onDeleteDevice: (id: string) => void;
  readonly onSaveStation: (original: string | null, station: { readonly id: string; readonly panel: string }) => void;
  readonly onDeleteStation: (id: string) => void;
}

type Tab = 'process' | 'variety' | 'device' | 'station';

const TABS: readonly (readonly [Tab, string])[] = [
  ['process', '加工の種類'],
  ['variety', '品種'],
  ['device', '装置'],
  ['station', 'ステーション'],
];

type Dialog =
  | { readonly kind: 'process'; readonly target: ProcessKind | null }
  | { readonly kind: 'variety'; readonly target: Variety | null }
  | { readonly kind: 'ids'; readonly target: string }
  | { readonly kind: 'device'; readonly target: DeviceConfig | null }
  | { readonly kind: 'station'; readonly target: StationConfig | null }
  | { readonly kind: 'delete'; readonly what: string; readonly name: string; readonly run: () => void };

const NO_PERMISSION = '消耗品の構成は、管理者のロールで使えます。';
const num = 'font-number text-[17px] font-semibold';

/** 消耗品の構成（docs/screens.md の「8 消耗品の構成」）。加工の種類、品種、装置、ステーションを登録する。 */
export function ConfigScreen(props: ConfigScreenProps) {
  const [tab, setTab] = useState<Tab>('process');
  const [dialog, setDialog] = useState<Dialog | null>(null);
  const allowed = props.permissions.has('configure');
  const blocked = allowed ? null : NO_PERMISSION;
  const close = () => setDialog(null);
  const usedPanels = [...props.devices.map((d) => d.panel), ...props.stations.map((s) => s.panel)];
  const askDelete = (what: string, name: string, run: () => void) => setDialog({ kind: 'delete', what, name, run });

  const rowActions = (reason: string | null, onChange: () => void, onDelete: () => void, extra?: ReactNode) => (
    <RowActions reason={allowed ? reason : null}>
      {extra}
      <button type="button" disabled={!allowed} onClick={onChange} className={plainButton}>
        変える
      </button>
      <button type="button" disabled={!allowed || reason !== null} onClick={onDelete} className={plainButton}>
        消す
      </button>
    </RowActions>
  );

  return (
    <>
      {blocked !== null && <p className="m-0 text-sm font-bold">{blocked}</p>}
      <div role="tablist" aria-label="登録するもの" className="flex flex-wrap gap-1 border-b-2 border-line">
        {TABS.map(([key, label]) => (
          <button
            key={key}
            type="button"
            role="tab"
            aria-selected={tab === key}
            onClick={() => setTab(key)}
            className={`-mb-0.5 min-h-12 border-b-3 px-4.5 text-[15px] font-bold focus-visible:outline-3 focus-visible:outline-focus ${tab === key ? 'border-graphite text-graphite' : 'border-transparent text-ink-muted'}`}
          >
            {label}
          </button>
        ))}
      </div>

      {tab === 'process' && (
        <Table
          note="加工の種類ごとに、運び方と、キャリアに入る個数を決めます。"
          addLabel="加工の種類を加える"
          allowed={allowed}
          onAdd={() => setDialog({ kind: 'process', target: null })}
          columns={['加工の種類', '運び方', 'キャリアの種類', 'キャリアに入る個数']}
        >
          {props.processKinds.map((k) => (
            <tr key={k.name} className="border-b border-disabled-border">
              <td className={`${td} font-bold`}>{k.name}</td>
              <td className={td}>{carryMethodLabel[k.method]}</td>
              <td className={td}>{k.carrier}</td>
              <td className={`${td} ${num}`}>{k.capacity}</td>
              <td className="px-3 py-1.5">
                {rowActions(
                  processKindDeleteBlockedReason(k.name, props.varieties),
                  () => setDialog({ kind: 'process', target: k }),
                  () => askDelete('加工の種類', k.name, () => props.onDeleteProcessKind(k.name)),
                )}
              </td>
            </tr>
          ))}
        </Table>
      )}

      {tab === 'variety' && (
        <Table
          note="品種の間に互換性はありません。ステーションで読んだ ID をどの品種にするか（ID と品種の対応）を、前もって登録しておきます。"
          addLabel="品種を加える"
          allowed={allowed}
          onAdd={() => setDialog({ kind: 'variety', target: null })}
          columns={['品種', '加工の種類', '対応させた ID']}
        >
          {[...props.varieties]
            .sort((a, b) => byIdNumber(a.name, b.name))
            .map((v) => (
              <tr key={v.name} className="border-b border-disabled-border">
                <td className={`${td} font-bold`}>{v.name}</td>
                <td className={td}>{v.processKind}</td>
                <td className={`${td} ${num}`}>{v.ids.length}</td>
                <td className="px-3 py-1.5">
                  {rowActions(
                    varietyDeleteBlockedReason(v),
                    () => setDialog({ kind: 'variety', target: v }),
                    () => askDelete('品種', v.name, () => props.onDeleteVariety(v.name)),
                    <button type="button" disabled={!allowed} onClick={() => setDialog({ kind: 'ids', target: v.name })} className={plainButton}>
                      ID の対応
                    </button>,
                  )}
                </td>
              </tr>
            ))}
        </Table>
      )}

      {tab === 'device' && (
        <Table
          note="装置ごとに、受け渡しパネルと、ストッカーごとのスロットの数を決めます。プレートに載せたまま置く装置には、スロットがありません。"
          addLabel="装置を加える"
          allowed={allowed}
          onAdd={() => setDialog({ kind: 'device', target: null })}
          columns={['装置', '受け渡しパネル', 'ストッカー']}
        >
          {[...props.devices]
            .sort((a, b) => byIdNumber(a.id, b.id))
            .map((d) => (
              <tr key={d.id} className="border-b border-disabled-border">
                <td className={`${td} font-number text-[17px] font-bold`}>{d.id}</td>
                <td className={td}>{d.panel}</td>
                <td className={td}>
                  {d.layout.kind === 'plate'
                    ? `プレートに載せたまま置く（ストッカー ${d.layout.stockers} つ）`
                    : d.layout.slots.map((n, i) => `ストッカー${i + 1}：スロット ${n}`).join('、')}
                </td>
                <td className="px-3 py-1.5">
                  {rowActions(
                    deviceDeleteBlockedReason(d),
                    () => setDialog({ kind: 'device', target: d }),
                    () => askDelete('装置', d.id, () => props.onDeleteDevice(d.id)),
                  )}
                </td>
              </tr>
            ))}
        </Table>
      )}

      {tab === 'station' && (
        <Table
          note="ステーションの仕様が決まるまでは、スペースの数は扱いません。"
          addLabel="ステーションを加える"
          allowed={allowed}
          onAdd={() => setDialog({ kind: 'station', target: null })}
          columns={['ステーション', '受け渡しパネル']}
        >
          {[...props.stations]
            .sort((a, b) => byIdNumber(a.id, b.id))
            .map((s) => (
              <tr key={s.id} className="border-b border-disabled-border">
                <td className={`${td} font-number text-[17px] font-bold`}>{s.id}</td>
                <td className={td}>{s.panel}</td>
                <td className="px-3 py-1.5">
                  {rowActions(
                    stationDeleteBlockedReason(s),
                    () => setDialog({ kind: 'station', target: s }),
                    () => askDelete('ステーション', s.id, () => props.onDeleteStation(s.id)),
                  )}
                </td>
              </tr>
            ))}
        </Table>
      )}

      {dialog?.kind === 'delete' && (
        <ConfirmDialog
          title={`${dialog.name} を消しますか？`}
          body={`この${dialog.what}の登録を消します。消した登録は、元に戻せません。`}
          confirmLabel={`${dialog.what}を消す`}
          tone="danger"
          onConfirm={() => {
            dialog.run();
            close();
          }}
          onCancel={close}
        />
      )}
      {dialog?.kind === 'process' && (
        <ProcessKindDialog
          target={dialog.target}
          existing={props.processKinds.map((k) => k.name)}
          carriers={props.carriers}
          onSave={(k) => {
            props.onSaveProcessKind(dialog.target?.name ?? null, k);
            close();
          }}
          onCancel={close}
        />
      )}
      {dialog?.kind === 'variety' && (
        <VarietyDialog
          target={dialog.target}
          existing={props.varieties.map((v) => v.name)}
          processKinds={props.processKinds.map((k) => k.name)}
          onSave={(v) => {
            props.onSaveVariety(dialog.target?.name ?? null, v);
            close();
          }}
          onCancel={close}
        />
      )}
      {dialog?.kind === 'ids' && (
        <IdsDialog variety={dialog.target} varieties={props.varieties} onAdd={props.onAddId} onRemove={props.onRemoveId} onClose={close} />
      )}
      {dialog?.kind === 'device' && (
        <DeviceDialog
          target={dialog.target}
          existing={props.devices.map((d) => d.id)}
          panels={panelChoices(props.transferPanels, usedPanels, dialog.target?.panel ?? null)}
          onSave={(d) => {
            props.onSaveDevice(dialog.target?.id ?? null, d);
            close();
          }}
          onCancel={close}
        />
      )}
      {dialog?.kind === 'station' && (
        <StationDialog
          target={dialog.target}
          existing={props.stations.map((s) => s.id)}
          panels={panelChoices(props.transferPanels, usedPanels, dialog.target?.panel ?? null)}
          onSave={(s) => {
            props.onSaveStation(dialog.target?.id ?? null, s);
            close();
          }}
          onCancel={close}
        />
      )}
    </>
  );
}

function Table({
  note: text,
  addLabel,
  allowed,
  onAdd,
  columns,
  children,
}: {
  readonly note: string;
  readonly addLabel: string;
  readonly allowed: boolean;
  readonly onAdd: () => void;
  readonly columns: readonly string[];
  readonly children: ReactNode;
}) {
  return (
    <section className={section}>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="m-0 max-w-[70ch] text-sm text-ink-muted">{text}</p>
        <button type="button" disabled={!allowed} onClick={onAdd} className={plainButton}>
          {addLabel}
        </button>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[720px] border-collapse text-sm">
          <thead>
            <tr>
              {columns.map((c) => (
                <th key={c} scope="col" className={th}>
                  {c}
                </th>
              ))}
              <th scope="col" className={th}>
                <span className="sr-only">操作</span>
              </th>
            </tr>
          </thead>
          <tbody>{children}</tbody>
        </table>
      </div>
    </section>
  );
}

function ProcessKindDialog({
  target,
  existing,
  carriers,
  onSave,
  onCancel,
}: {
  readonly target: ProcessKind | null;
  readonly existing: readonly string[];
  readonly carriers: readonly string[];
  readonly onSave: (kind: ProcessKind) => void;
  readonly onCancel: () => void;
}) {
  const [draft, setDraft] = useState<ProcessKindDraft>(
    target === null ? { name: '', method: '', carrier: '', capacity: '' } : { name: target.name, method: target.method, carrier: target.carrier, capacity: String(target.capacity) },
  );
  const problem = processKindProblem(draft, existing, target?.name ?? null);
  return (
    <FormDialog title={target === null ? '加工の種類を加える' : `${target.name} を変える`} onCancel={onCancel}>
      {target === null && <TextField label="加工の種類の名前" value={draft.name} onChange={(name) => setDraft((d) => ({ ...d, name }))} />}
      <Choice
        label="運び方"
        value={draft.method}
        options={(Object.keys(carryMethodLabel) as CarryMethod[]).map((m) => [m, carryMethodLabel[m]] as const)}
        onChange={(method) => setDraft((d) => ({ ...d, method: method as CarryMethod | '' }))}
      />
      <Choice label="キャリアの種類" value={draft.carrier} options={carriers} onChange={(carrier) => setDraft((d) => ({ ...d, carrier }))} />
      <TextField label="キャリアに入る個数" numeric value={draft.capacity} onChange={(capacity) => setDraft((d) => ({ ...d, capacity }))} />
      <FormFooter
        problem={problem}
        saveLabel="加工の種類を保存する"
        onCancel={onCancel}
        onSave={() => {
          if (draft.method !== '') {
            onSave({ name: draft.name.trim(), method: draft.method, carrier: draft.carrier, capacity: Number(draft.capacity) });
          }
        }}
      />
    </FormDialog>
  );
}

function VarietyDialog({
  target,
  existing,
  processKinds,
  onSave,
  onCancel,
}: {
  readonly target: Variety | null;
  readonly existing: readonly string[];
  readonly processKinds: readonly string[];
  readonly onSave: (variety: { readonly name: string; readonly processKind: string }) => void;
  readonly onCancel: () => void;
}) {
  const [draft, setDraft] = useState({ name: target?.name ?? '', processKind: target?.processKind ?? '' });
  const problem = varietyProblem(draft, existing, target);
  return (
    <FormDialog title={target === null ? '品種を加える' : `${target.name} を変える`} onCancel={onCancel}>
      {target === null && <TextField label="品種の名前" value={draft.name} onChange={(name) => setDraft((d) => ({ ...d, name }))} />}
      <Choice label="加工の種類" value={draft.processKind} options={processKinds} onChange={(processKind) => setDraft((d) => ({ ...d, processKind }))} />
      <FormFooter problem={problem} saveLabel="品種を保存する" onCancel={onCancel} onSave={() => onSave({ name: draft.name.trim(), processKind: draft.processKind })} />
    </FormDialog>
  );
}

function IdsDialog({
  variety: name,
  varieties,
  onAdd,
  onRemove,
  onClose,
}: {
  readonly variety: string;
  readonly varieties: readonly Variety[];
  readonly onAdd: (variety: string, id: string) => void;
  readonly onRemove: (variety: string, id: string) => void;
  readonly onClose: () => void;
}) {
  const [id, setId] = useState('');
  const variety = varieties.find((v) => v.name === name);
  if (variety === undefined) {
    return null;
  }
  const problem = id.trim() === '' ? null : idProblem(id, varieties);
  return (
    <FormDialog title={`${name} の ID の対応`} onCancel={onClose}>
      <p className={note}>ステーションで読んだ ID が、ここにあれば、{name} の新品として入庫します。在庫にある ID は外せません。</p>
      <div className="flex items-end gap-2">
        <div className="flex-1">
          <TextField label="加える ID" value={id} onChange={setId} />
        </div>
        <button
          type="button"
          disabled={id.trim() === '' || problem !== null}
          onClick={() => {
            onAdd(name, id.trim());
            setId('');
          }}
          className={darkButton}
        >
          加える
        </button>
      </div>
      {problem !== null && <p className="m-0 text-sm text-ink-muted">{problem}</p>}
      {variety.ids.length === 0 ? (
        <p className="m-0 text-[15px] text-ink-muted">まだ ID を対応させていません。</p>
      ) : (
        <ul className="m-0 flex max-h-72 list-none flex-col overflow-y-auto p-0">
          {[...variety.ids].sort(byIdNumber).map((i) => {
            const reason = removeIdBlockedReason(variety, i);
            return (
              <li key={i} className="flex flex-wrap items-center justify-between gap-2 border-b border-disabled-border py-1.5">
                <span className="font-number text-[17px] font-semibold">{i}</span>
                <span className="flex items-center gap-2">
                  {reason !== null && <span className={note}>在庫にある</span>}
                  <button type="button" disabled={reason !== null} title={reason ?? undefined} onClick={() => onRemove(name, i)} className={plainButton}>
                    外す
                  </button>
                </span>
              </li>
            );
          })}
        </ul>
      )}
      <div className="flex justify-end">
        <button type="button" onClick={onClose} className={plainButton}>
          閉じる
        </button>
      </div>
    </FormDialog>
  );
}

function DeviceDialog({
  target,
  existing,
  panels,
  onSave,
  onCancel,
}: {
  readonly target: DeviceConfig | null;
  readonly existing: readonly string[];
  readonly panels: readonly string[];
  readonly onSave: (device: { readonly id: string; readonly panel: string; readonly layout: DeviceLayout }) => void;
  readonly onCancel: () => void;
}) {
  const [draft, setDraft] = useState<DeviceDraft>(() => (target === null ? { id: '', panel: '', plate: false, slots: [''], plateStockers: '' } : deviceDraftOf(target)));
  const problem = deviceProblem(draft, existing, target);
  const setSlot = (i: number, v: string) => setDraft((d) => ({ ...d, slots: d.slots.map((s, j) => (j === i ? v : s)) }));

  return (
    <FormDialog title={target === null ? '装置を加える' : `${target.id} を変える`} onCancel={onCancel}>
      {target === null && <TextField label="装置の ID" value={draft.id} onChange={(id) => setDraft((d) => ({ ...d, id }))} />}
      <Choice label="受け渡しパネル" value={draft.panel} options={panels} onChange={(panel) => setDraft((d) => ({ ...d, panel }))} />
      <label className="flex min-h-11 items-center gap-2 text-[15px]">
        <input type="checkbox" checked={draft.plate} onChange={(e) => setDraft((d) => ({ ...d, plate: e.target.checked }))} className="size-5 accent-graphite" />
        プレートに載せたまま置く（スロットがない）
      </label>
      {draft.plate ? (
        <TextField label="ストッカーの数" numeric value={draft.plateStockers} onChange={(plateStockers) => setDraft((d) => ({ ...d, plateStockers }))} />
      ) : (
        <div className="flex flex-col gap-2">
          {draft.slots.map((s, i) => (
            <div key={i} className="flex items-end gap-2">
              <div className="flex-1">
                <TextField label={`ストッカー${i + 1} のスロットの数`} numeric value={s} onChange={(v) => setSlot(i, v)} />
              </div>
              <button
                type="button"
                aria-label={`ストッカー${i + 1} を外す`}
                disabled={draft.slots.length === 1}
                onClick={() => setDraft((d) => ({ ...d, slots: d.slots.filter((_, j) => j !== i) }))}
                className={plainButton}
              >
                外す
              </button>
            </div>
          ))}
          <div>
            <button type="button" onClick={() => setDraft((d) => ({ ...d, slots: [...d.slots, ''] }))} className={plainButton}>
              ストッカーを加える
            </button>
          </div>
        </div>
      )}
      <FormFooter problem={problem} saveLabel="装置を保存する" onCancel={onCancel} onSave={() => onSave({ id: draft.id.trim(), panel: draft.panel, layout: toDeviceLayout(draft) })} />
    </FormDialog>
  );
}

function StationDialog({
  target,
  existing,
  panels,
  onSave,
  onCancel,
}: {
  readonly target: StationConfig | null;
  readonly existing: readonly string[];
  readonly panels: readonly string[];
  readonly onSave: (station: { readonly id: string; readonly panel: string }) => void;
  readonly onCancel: () => void;
}) {
  const [draft, setDraft] = useState({ id: target?.id ?? '', panel: target?.panel ?? '' });
  const problem = stationProblem(draft, existing, target);
  return (
    <FormDialog title={target === null ? 'ステーションを加える' : `${target.id} を変える`} onCancel={onCancel}>
      {target === null && <TextField label="ステーションの ID" value={draft.id} onChange={(id) => setDraft((d) => ({ ...d, id }))} />}
      <Choice label="受け渡しパネル" value={draft.panel} options={panels} onChange={(panel) => setDraft((d) => ({ ...d, panel }))} />
      <FormFooter problem={problem} saveLabel="ステーションを保存する" onCancel={onCancel} onSave={() => onSave({ id: draft.id.trim(), panel: draft.panel })} />
    </FormDialog>
  );
}
