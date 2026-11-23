import { useState } from 'react';
import {
  agvChangeBlockedReason,
  agvProblem,
  configureBlockedReason,
  registrationLabel,
  sortAgvEntries,
  toAgvType,
  typeDeleteBlockedReason,
  typeDraftOf,
  typeProblem,
  type AgvDraft,
  type AgvEntry,
  type AgvType,
  type ChargeDefaults,
  type TypeDraft,
} from './agvs';
import { ConfirmDialog } from './ConfirmDialog';
import { controlStatusLabel } from './controlStatus';
import { Choice, darkButton, FormDialog, FormFooter, note, plainButton, RowActions, section, td, TextField, th } from './forms';
import type { Permissions } from './permissions';

export interface AgvsScreenProps {
  readonly entries: readonly AgvEntry[];
  readonly types: readonly AgvType[];
  /** 登録してあるキャリアの種類。 */
  readonly carriers: readonly string[];
  /** 充電の値の、いまの既定値（種類を加えるときに、空の値を埋める）。 */
  readonly defaults: ChargeDefaults;
  readonly permissions: Permissions;
  readonly onAddAgv: (agv: AgvDraft) => void;
  readonly onChangeAgv: (id: string, change: { readonly type: string; readonly carrier: string }) => void;
  /** 登録の食い違いをそろえる（足りない側に登録する）。 */
  readonly onAlignAgv: (id: string, change: { readonly type: string; readonly carrier: string }) => void;
  readonly onDeleteAgv: (id: string) => void;
  /** 種類を保存する。original は直す前の名前（加えるときは null）。 */
  readonly onSaveType: (original: string | null, type: AgvType) => void;
  readonly onDeleteType: (name: string) => void;
}

type Dialog =
  | { readonly kind: 'addAgv' }
  | { readonly kind: 'changeAgv' | 'alignAgv'; readonly entry: AgvEntry }
  | { readonly kind: 'deleteAgv'; readonly entry: AgvEntry }
  | { readonly kind: 'addType' }
  | { readonly kind: 'changeType' | 'deleteType'; readonly type: AgvType };

/** AGV と AGV の種類の登録（docs/screens.md の「7 AGV と AGV の種類の登録」）。 */
export function AgvsScreen(props: AgvsScreenProps) {
  const { entries, types, permissions } = props;
  const [dialog, setDialog] = useState<Dialog | null>(null);
  const blocked = configureBlockedReason(permissions);
  const close = () => setDialog(null);

  return (
    <>
      {blocked !== null && <p className="m-0 text-sm font-bold">{blocked}</p>}
      <section aria-labelledby="agv-title" className={section}>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 id="agv-title" className="m-0 text-lg font-bold">
            AGV
          </h2>
          <button type="button" disabled={blocked !== null} onClick={() => setDialog({ kind: 'addAgv' })} className={plainButton}>
            AGV を加える
          </button>
        </div>
        <p className="m-0 text-sm text-ink-muted">新しく加えた AGV は、切り離しから始めます。マップの上に置いて位置リセットをしてから、コントロールステータスを切り替えてください。</p>
        <div className="relative overflow-x-auto">
          <table className="w-full min-w-[820px] border-collapse text-sm">
            <thead>
              <tr>
                <th scope="col" className={th}>
                  AGV
                </th>
                <th scope="col" className={th}>
                  AGV の種類
                </th>
                <th scope="col" className={th}>
                  キャリアの種類
                </th>
                <th scope="col" className={th}>
                  コントロールステータス
                </th>
                <th scope="col" className={th}>
                  登録
                </th>
                <th scope="col" className={th}>
                  <span className="sr-only">操作</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {sortAgvEntries(entries).map((e) => {
                const odd = e.registration !== 'both';
                const reason = odd ? blocked : agvChangeBlockedReason(e, permissions);
                return (
                  <tr key={e.id} className={`border-b border-disabled-border ${odd ? 'bg-white' : ''}`}>
                    <td className={`${td} font-number text-[17px] font-bold`}>{e.id}</td>
                    <td className={td}>{e.type ?? '－'}</td>
                    <td className={td}>{e.carrier ?? '－'}</td>
                    <td className={td}>{e.control === null ? '－' : controlStatusLabel[e.control]}</td>
                    <td className={`${td} font-bold ${odd ? 'text-attention' : 'text-running'}`}>{registrationLabel[e.registration]}</td>
                    <td className="px-3 py-1.5">
                      <RowActions reason={blocked === null ? reason : null}>
                        {odd ? (
                          <button type="button" disabled={reason !== null} onClick={() => setDialog({ kind: 'alignAgv', entry: e })} className={darkButton}>
                            登録をそろえる
                          </button>
                        ) : (
                          <button type="button" disabled={reason !== null} onClick={() => setDialog({ kind: 'changeAgv', entry: e })} className={plainButton}>
                            変える
                          </button>
                        )}
                        <button type="button" disabled={reason !== null} onClick={() => setDialog({ kind: 'deleteAgv', entry: e })} className={plainButton}>
                          消す
                        </button>
                      </RowActions>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </section>

      <section aria-labelledby="type-title" className={section}>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 id="type-title" className="m-0 text-lg font-bold">
            AGV の種類
          </h2>
          <button type="button" disabled={blocked !== null} onClick={() => setDialog({ kind: 'addType' })} className={plainButton}>
            種類を加える
          </button>
        </div>
        <p className="m-0 text-sm text-ink-muted">変えた値は、次の割り当てと充電の判断から効きます。</p>
        <div className="relative overflow-x-auto">
          <table className="w-full min-w-[960px] border-collapse text-sm">
            <thead>
              <tr>
                {['種類', '前進の速さ', '後退の速さ', '回転の速さ', '充電に向かう残量', '割り当てに戻す残量', '特殊消耗品受け渡しパネル'].map((h) => (
                  <th key={h} scope="col" className={th}>
                    {h}
                  </th>
                ))}
                <th scope="col" className={th}>
                  <span className="sr-only">操作</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {types.map((t) => {
                const reason = typeDeleteBlockedReason(t, entries, permissions);
                return (
                  <tr key={t.name} className="border-b border-disabled-border">
                    <td className={`${td} font-bold`}>{t.name}</td>
                    <td className={`${td} font-number text-base font-semibold`}>{t.forward} mm/s</td>
                    <td className={`${td} font-number text-base font-semibold`}>{t.backward} mm/s</td>
                    <td className={`${td} font-number text-base font-semibold`}>{t.rotation} °/s</td>
                    <td className={`${td} font-number text-base font-semibold`}>{t.chargeAt}%</td>
                    <td className={`${td} font-number text-base font-semibold`}>{t.resumeAt}%</td>
                    <td className={td}>{t.special ? '走れる' : '走れない'}</td>
                    <td className="px-3 py-1.5">
                      <RowActions reason={blocked === null ? reason : null}>
                        <button type="button" disabled={blocked !== null} onClick={() => setDialog({ kind: 'changeType', type: t })} className={plainButton}>
                          変える
                        </button>
                        <button type="button" disabled={reason !== null} onClick={() => setDialog({ kind: 'deleteType', type: t })} className={plainButton}>
                          消す
                        </button>
                      </RowActions>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </section>

      {(dialog?.kind === 'addAgv' || dialog?.kind === 'changeAgv' || dialog?.kind === 'alignAgv') && (
        <AgvDialog
          dialog={dialog}
          existingIds={entries.map((e) => e.id)}
          types={types.map((t) => t.name)}
          carriers={props.carriers}
          onSave={(draft) => {
            if (dialog.kind === 'addAgv') {
              props.onAddAgv(draft);
            } else {
              (dialog.kind === 'alignAgv' ? props.onAlignAgv : props.onChangeAgv)(dialog.entry.id, { type: draft.type, carrier: draft.carrier });
            }
            close();
          }}
          onCancel={close}
        />
      )}
      {dialog?.kind === 'deleteAgv' && (
        <ConfirmDialog
          title={`${dialog.entry.id} を消しますか？`}
          body="交通整理と配車の両方から、この AGV の登録を消します。消した登録は、元に戻せません。"
          confirmLabel="AGV を消す"
          tone="danger"
          onConfirm={() => {
            props.onDeleteAgv(dialog.entry.id);
            close();
          }}
          onCancel={close}
        />
      )}
      {(dialog?.kind === 'addType' || dialog?.kind === 'changeType') && (
        <TypeDialog
          type={dialog.kind === 'changeType' ? dialog.type : null}
          existingNames={types.map((t) => t.name)}
          defaults={props.defaults}
          onSave={(type) => {
            props.onSaveType(dialog.kind === 'changeType' ? dialog.type.name : null, type);
            close();
          }}
          onCancel={close}
        />
      )}
      {dialog?.kind === 'deleteType' && (
        <ConfirmDialog
          title={`${dialog.type.name} を消しますか？`}
          body="この AGV の種類を消します。消した種類は、元に戻せません。"
          confirmLabel="種類を消す"
          tone="danger"
          onConfirm={() => {
            props.onDeleteType(dialog.type.name);
            close();
          }}
          onCancel={close}
        />
      )}
    </>
  );
}

function AgvDialog({
  dialog,
  existingIds,
  types,
  carriers,
  onSave,
  onCancel,
}: {
  readonly dialog: { readonly kind: 'addAgv' } | { readonly kind: 'changeAgv' | 'alignAgv'; readonly entry: AgvEntry };
  readonly existingIds: readonly string[];
  readonly types: readonly string[];
  readonly carriers: readonly string[];
  readonly onSave: (draft: AgvDraft) => void;
  readonly onCancel: () => void;
}) {
  const entry = dialog.kind === 'addAgv' ? null : dialog.entry;
  const [draft, setDraft] = useState<AgvDraft>({ id: entry?.id ?? '', type: entry?.type ?? '', carrier: entry?.carrier ?? '' });
  const problem = agvProblem(draft, entry === null ? existingIds : null);
  const showType = dialog.kind !== 'alignAgv' || entry?.type === null;
  const showCarrier = dialog.kind !== 'alignAgv' || entry?.carrier === null;
  const title = entry === null ? 'AGV を加える' : dialog.kind === 'alignAgv' ? `${entry.id} の登録をそろえる` : `${entry.id} を変える`;

  return (
    <FormDialog title={title} onCancel={onCancel}>
      <p className={note}>
        {dialog.kind === 'alignAgv'
          ? `${entry?.registration === 'trafficOnly' ? '配車' : '走行'}の側に足りない項目を入れて、両方に登録します。そろえた AGV は、切り離しから始めます。`
          : '保存した AGV は、切り離しのまま、両方（走行と配車）に登録します。'}
      </p>
      {entry === null && (
        <TextField label="AGV の番号" value={draft.id} onChange={(id) => setDraft((d) => ({ ...d, id }))} />
      )}
      {showType && <Choice label="AGV の種類" value={draft.type} options={types} onChange={(type) => setDraft((d) => ({ ...d, type }))} />}
      {showCarrier && <Choice label="キャリアの種類" value={draft.carrier} options={carriers} onChange={(carrier) => setDraft((d) => ({ ...d, carrier }))} />}
      <FormFooter problem={problem} saveLabel="AGV を保存する" onCancel={onCancel} onSave={() => onSave({ ...draft, id: draft.id.trim() })} />
    </FormDialog>
  );
}

const TYPE_FIELDS: readonly (readonly [keyof Omit<TypeDraft, 'name' | 'special'>, string, string])[] = [
  ['forward', '前進の速さ', 'mm/s'],
  ['backward', '後退の速さ', 'mm/s'],
  ['rotation', '回転の速さ', '°/s'],
  ['chargeAt', '充電に向かう残量', '%'],
  ['resumeAt', '割り当てに戻す残量', '%'],
];

function TypeDialog({
  type,
  existingNames,
  defaults,
  onSave,
  onCancel,
}: {
  readonly type: AgvType | null;
  readonly existingNames: readonly string[];
  readonly defaults: ChargeDefaults;
  readonly onSave: (type: AgvType) => void;
  readonly onCancel: () => void;
}) {
  const [draft, setDraft] = useState<TypeDraft>(() =>
    type === null ? { name: '', forward: '', backward: '', rotation: '', chargeAt: '', resumeAt: '', special: false } : typeDraftOf(type),
  );
  const problem = typeProblem(draft, type === null ? { mode: 'add', existingNames, defaults } : { mode: 'edit', existingNames, defaults, original: type.name });
  const placeholder = (key: string) => (type === null && key === 'chargeAt' ? `既定値 ${defaults.chargeAt}` : type === null && key === 'resumeAt' ? `既定値 ${defaults.resumeAt}` : undefined);

  return (
    <FormDialog title={type === null ? 'AGV の種類を加える' : `${type.name} を変える`} onCancel={onCancel}>
      {type === null && <p className={note}>充電の値を空にすると、いまの既定値で埋めて保存します。あとで既定値を変えても、この種類の値は変わりません。</p>}
      <TextField label="種類の名前" value={draft.name} onChange={(name) => setDraft((d) => ({ ...d, name }))} />
      <div className="grid grid-cols-2 gap-3 max-sm:grid-cols-1">
        {TYPE_FIELDS.map(([key, label, unit]) => (
          <TextField key={key} label={label} unit={unit} numeric placeholder={placeholder(key)} value={draft[key]} onChange={(v) => setDraft((d) => ({ ...d, [key]: v }))} />
        ))}
      </div>
      <label className="flex min-h-11 items-center gap-2 text-[15px]">
        <input type="checkbox" checked={draft.special} onChange={(e) => setDraft((d) => ({ ...d, special: e.target.checked }))} className="size-5 accent-graphite" />
        特殊消耗品受け渡しパネルを走れる
      </label>
      <FormFooter problem={problem} saveLabel="種類を保存する" onCancel={onCancel} onSave={() => onSave(toAgvType(draft, defaults))} />
    </FormDialog>
  );
}
