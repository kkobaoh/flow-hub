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
  typeNameOf,
  typeProblem,
  type AgvDraft,
  type AgvEntry,
  type AgvType,
  type ChargeDefaults,
  type TypeDraft,
} from '../features/fleet/agvs';
import { ConfirmDialog } from '../shared/components/ConfirmDialog';
import { controlStatusLabel } from '../features/fleet/controlStatus';
import { Choice, darkButton, FormDialog, FormFooter, note, plainButton, RowActions, section, td, TextField, th } from '../shared/components/forms';
import type { Permissions } from '../features/auth/permissions';

export interface FleetPageProps {
  readonly entries: readonly AgvEntry[];
  readonly types: readonly AgvType[];
  /** 登録してあるキャリアの種類。 */
  readonly carriers: readonly string[];
  /** 充電の値の、いまの既定値（種類を加えるときに、空の値を埋める）。 */
  readonly defaults: ChargeDefaults;
  readonly permissions: Permissions;
  /** サーバーとつながっていない（種類の操作を送れない）。 */
  readonly offline: boolean;
  /** AGV を加える。受け付けたら null、断られたら理由の文を返す（AGV の操作は、どれも同じ）。 */
  readonly onAddAgv: (agv: AgvDraft) => Promise<string | null>;
  /** 種類とキャリアの種類を変える（種類が未定の AGV に入れるときも）。 */
  readonly onChangeAgv: (id: string, change: { readonly type: string; readonly carrier: string }) => Promise<string | null>;
  /** 登録の食い違いをそろえる（足りない側に登録する）。 */
  readonly onAlignAgv: (id: string, change: { readonly type: string; readonly carrier: string }) => Promise<string | null>;
  readonly onDeleteAgv: (id: string) => Promise<string | null>;
  /** 種類を保存する。original は変える種類の ID（加えるときは null）。受け付けたら null、断られたら理由の文を返す。 */
  readonly onSaveType: (original: string | null, type: AgvType) => Promise<string | null>;
  /** 種類を消す。受け付けたら null、断られたら理由の文を返す。 */
  readonly onDeleteType: (id: string) => Promise<string | null>;
}

type Dialog =
  | { readonly kind: 'addAgv' }
  | { readonly kind: 'changeAgv' | 'alignAgv'; readonly entry: AgvEntry }
  | { readonly kind: 'deleteAgv'; readonly entry: AgvEntry }
  | { readonly kind: 'addType' }
  | { readonly kind: 'changeType' | 'deleteType'; readonly type: AgvType };

/** AGV と AGV の種類の登録（docs/screens.md の「7 AGV と AGV の種類の登録」）。 */
export function FleetPage(props: FleetPageProps) {
  const { entries, types, permissions } = props;
  const [dialog, setDialog] = useState<Dialog | null>(null);
  const blocked = configureBlockedReason(permissions);
  const agvBlocked = blocked ?? (props.offline ? 'サーバーとつながっていないので、AGV を加えたり変えたりできません。' : null);
  const typeBlocked = blocked ?? (props.offline ? 'サーバーとつながっていないので、種類を加えたり変えたりできません。' : null);
  // 種類の操作は、送って結果を待つあいだは押せなくする。断られたら、理由を出す（次に送ったら消す）
  const [sending, setSending] = useState(false);
  const [notice, setNotice] = useState<{ readonly about: 'agv' | 'type'; readonly text: string } | null>(null);
  const close = () => setDialog(null);
  /** 送って、断られたら、その欄（AGV か種類）に理由を出す。 */
  const send = async (about: 'agv' | 'type', operation: () => Promise<string | null>) => {
    setSending(true);
    setNotice(null);
    try {
      const text = await operation();
      setNotice(text === null ? null : { about, text });
    } finally {
      setSending(false);
    }
  };
  const noticeOf = (about: 'agv' | 'type') =>
    notice?.about === about && (
      <p role="alert" className="m-0 text-sm font-bold text-attention">
        {notice.text}
      </p>
    );

  return (
    <>
      {blocked !== null && <p className="m-0 text-sm font-bold">{blocked}</p>}
      <section aria-labelledby="agv-title" className={section}>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 id="agv-title" className="m-0 text-lg font-bold">
            AGV
          </h2>
          <button type="button" disabled={agvBlocked !== null || sending} onClick={() => setDialog({ kind: 'addAgv' })} className={plainButton}>
            AGV を加える
          </button>
        </div>
        {blocked === null && agvBlocked !== null && <p className="m-0 text-sm font-bold">{agvBlocked}</p>}
        {noticeOf('agv')}
        <p className="m-0 text-sm text-ink-muted">新しく加えた AGV と、初めて通信してきて自動で登録した AGV は、切り離しから始めます（自動で登録した AGV は、種類とキャリアの種類を入れるまで動かしません）。マップの上に置いて位置リセットをしてから、コントロールステータスを切り替えてください。</p>
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
                const odd = e.registration === 'trafficOnly' || e.registration === 'dispatchOnly';
                const unconfigured = e.registration === 'unconfigured';
                const reason = agvBlocked ?? (odd ? null : agvChangeBlockedReason(e, permissions));
                return (
                  <tr key={e.id} className={`border-b border-disabled-border ${odd || unconfigured ? 'bg-white' : ''}`}>
                    <td className={`${td} font-number text-[17px] font-bold`}>{e.id}</td>
                    <td className={td}>{e.type === null ? '－' : typeNameOf(types, e.type)}</td>
                    <td className={td}>{e.carrier ?? '－'}</td>
                    <td className={td}>{e.control === null ? '－' : controlStatusLabel[e.control]}</td>
                    <td className={`${td} font-bold ${odd || unconfigured ? 'text-attention' : 'text-running'}`}>{registrationLabel[e.registration]}</td>
                    <td className="px-3 py-1.5">
                      <RowActions reason={agvBlocked === null ? reason : null}>
                        {odd ? (
                          <button type="button" disabled={reason !== null || sending} onClick={() => setDialog({ kind: 'alignAgv', entry: e })} className={darkButton}>
                            登録をそろえる
                          </button>
                        ) : unconfigured ? (
                          <button type="button" disabled={reason !== null || sending} onClick={() => setDialog({ kind: 'changeAgv', entry: e })} className={darkButton}>
                            種類を入れる
                          </button>
                        ) : (
                          <button type="button" disabled={reason !== null || sending} onClick={() => setDialog({ kind: 'changeAgv', entry: e })} className={plainButton}>
                            変える
                          </button>
                        )}
                        <button type="button" disabled={reason !== null || sending} onClick={() => setDialog({ kind: 'deleteAgv', entry: e })} className={plainButton}>
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
          <button type="button" disabled={typeBlocked !== null || sending} onClick={() => setDialog({ kind: 'addType' })} className={plainButton}>
            種類を加える
          </button>
        </div>
        <p className="m-0 text-sm text-ink-muted">変えた値は、次の割り当てと充電の判断から効きます。</p>
        {blocked === null && typeBlocked !== null && <p className="m-0 text-sm font-bold">{typeBlocked}</p>}
        {noticeOf('type')}
        <div className="relative overflow-x-auto">
          <table className="w-full min-w-[960px] border-collapse text-sm">
            <thead>
              <tr>
                {['種類', 'ID', '前進の速さ', '後退の速さ', '回転の速さ', '充電に向かう残量', '割り当てに戻す残量', '特殊消耗品受け渡しパネル'].map((h) => (
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
                const reason = typeDeleteBlockedReason(t, entries, permissions) ?? typeBlocked;
                return (
                  <tr key={t.id} className="border-b border-disabled-border">
                    <td className={`${td} font-bold`}>{t.name}</td>
                    <td className={`${td} font-number`}>{t.id}</td>
                    <td className={`${td} font-number text-base font-semibold`}>{t.forward} mm/s</td>
                    <td className={`${td} font-number text-base font-semibold`}>{t.backward} mm/s</td>
                    <td className={`${td} font-number text-base font-semibold`}>{t.rotation} °/s</td>
                    <td className={`${td} font-number text-base font-semibold`}>{t.chargeAt}%</td>
                    <td className={`${td} font-number text-base font-semibold`}>{t.resumeAt}%</td>
                    <td className={td}>{t.special ? '走れる' : '走れない'}</td>
                    <td className="px-3 py-1.5">
                      <RowActions reason={blocked === null && typeBlocked === null ? reason : null}>
                        <button type="button" disabled={typeBlocked !== null || sending} onClick={() => setDialog({ kind: 'changeType', type: t })} className={plainButton}>
                          変える
                        </button>
                        <button type="button" disabled={reason !== null || sending} onClick={() => setDialog({ kind: 'deleteType', type: t })} className={plainButton}>
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
          types={types.map((t) => [t.id, t.name] as const)}
          carriers={props.carriers}
          onSave={(draft) => {
            const current = dialog;
            close();
            void send('agv', () =>
              current.kind === 'addAgv'
                ? props.onAddAgv(draft)
                : (current.kind === 'alignAgv' ? props.onAlignAgv : props.onChangeAgv)(current.entry.id, { type: draft.type, carrier: draft.carrier }),
            );
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
            const id = dialog.entry.id;
            close();
            void send('agv', () => props.onDeleteAgv(id));
          }}
          onCancel={close}
        />
      )}
      {(dialog?.kind === 'addType' || dialog?.kind === 'changeType') && (
        <TypeDialog
          type={dialog.kind === 'changeType' ? dialog.type : null}
          existingIds={types.map((t) => t.id)}
          // 変えるときは、自分の名前は数えない
          existingNames={types.filter((t) => dialog.kind !== 'changeType' || t.id !== dialog.type.id).map((t) => t.name)}
          defaults={props.defaults}
          onSave={(type) => {
            const original = dialog.kind === 'changeType' ? dialog.type.id : null;
            close();
            void send('type', () => props.onSaveType(original, type));
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
            const id = dialog.type.id;
            close();
            void send('type', () => props.onDeleteType(id));
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
  /** 種類（ID と名前）。 */
  readonly types: readonly (readonly [string, string])[];
  readonly carriers: readonly string[];
  readonly onSave: (draft: AgvDraft) => void;
  readonly onCancel: () => void;
}) {
  const entry = dialog.kind === 'addAgv' ? null : dialog.entry;
  const [draft, setDraft] = useState<AgvDraft>({ id: entry?.id ?? '', type: entry?.type ?? '', carrier: entry?.carrier ?? '' });
  const problem = agvProblem(draft, entry === null ? existingIds : null);
  const showType = dialog.kind !== 'alignAgv' || entry?.type === null;
  const showCarrier = dialog.kind !== 'alignAgv' || entry?.carrier === null;
  const title =
    entry === null
      ? 'AGV を加える'
      : dialog.kind === 'alignAgv'
        ? `${entry.id} の登録をそろえる`
        : entry.registration === 'unconfigured'
          ? `${entry.id} の種類とキャリアの種類を入れる`
          : `${entry.id} を変える`;

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

const TYPE_FIELDS: readonly (readonly [keyof Omit<TypeDraft, 'id' | 'name' | 'special'>, string, string])[] = [
  ['forward', '前進の速さ', 'mm/s'],
  ['backward', '後退の速さ', 'mm/s'],
  ['rotation', '回転の速さ', '°/s'],
  ['chargeAt', '充電に向かう残量', '%'],
  ['resumeAt', '割り当てに戻す残量', '%'],
];

function TypeDialog({
  type,
  existingIds,
  existingNames,
  defaults,
  onSave,
  onCancel,
}: {
  readonly type: AgvType | null;
  readonly existingIds: readonly string[];
  /** ほかの種類の名前。 */
  readonly existingNames: readonly string[];
  readonly defaults: ChargeDefaults;
  readonly onSave: (type: AgvType) => void;
  readonly onCancel: () => void;
}) {
  const [draft, setDraft] = useState<TypeDraft>(() =>
    type === null ? { id: '', name: '', forward: '', backward: '', rotation: '', chargeAt: '', resumeAt: '', special: false } : typeDraftOf(type),
  );
  const problem = typeProblem(draft, type === null ? { mode: 'add', existingIds, existingNames, defaults } : { mode: 'edit', existingNames, defaults });
  const placeholder = (key: string) => (type === null && key === 'chargeAt' ? `既定値 ${defaults.chargeAt}` : type === null && key === 'resumeAt' ? `既定値 ${defaults.resumeAt}` : undefined);

  return (
    <FormDialog title={type === null ? 'AGV の種類を加える' : `${type.name} を変える`} onCancel={onCancel}>
      {type === null && <p className={note}>充電の値を空にすると、いまの既定値で埋めて保存します。あとで既定値を変えても、この種類の値は変わりません。</p>}
      {type === null ? (
        <TextField label="種類の ID" value={draft.id} onChange={(id) => setDraft((d) => ({ ...d, id }))} />
      ) : (
        <p className={note}>
          種類の ID：<span className="font-number font-bold">{type.id}</span>（ID は変えられません）
        </p>
      )}
      {type === null && <p className={note}>ID は、AGV が使う種類を指すものです。英数字と「-」「_」で入れてください。あとから変えられません。</p>}
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
