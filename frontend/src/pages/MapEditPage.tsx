import { useEffect, useState } from 'react';
import { crossPoint, type Rect } from '../features/routing/drawing';
import { equipmentId } from '../features/tool/equipment';
import { MapCanvas } from '../features/routing/MapCanvas';
import { heading, type PanelId, type PanelType } from '../features/routing/model';
import { ConfirmDialog } from '../shared/components/ConfirmDialog';
import { darkButton, field, note, plainButton, section, TextField } from '../shared/components/forms';
import {
  connect,
  connectionView,
  editBounds,
  isDraftChanged,
  panelTypeLabel,
  placePanel,
  publishBlockedReason,
  removeConnection,
  removePanel,
  renamePanel,
  setConnection,
  setLoweringHeading,
  setPanelType,
  updatePanel,
  type MapDraft,
} from '../features/routing/mapEdit';
import type { Permissions } from '../features/auth/permissions';
import { dateTimeOf } from '../shared/utils/dateTime';

/** 受け渡しパネルごとのキャリアの高さ（版とは別の設定）。null は設定なし（AGV が測る）。 */
export interface CarrierHeight {
  readonly heightMm: number;
  readonly source: 'manual' | 'measured';
  readonly at: Date;
}

export interface MapEditPageProps {
  /** 保存してある下書き（なければ、公開中の版の写し）。 */
  readonly saved: MapDraft;
  readonly draftVersion: number;
  /** 下書きを保存した日時（まだ保存していなければ null）。 */
  readonly savedAt: Date | null;
  readonly publishedVersion: number;
  /** 装置とステーションと、その受け渡しパネル。 */
  readonly equipment: readonly { readonly id: string; readonly panel: string }[];
  /** 公開中の版にある受け渡しパネルの、キャリアの高さ。 */
  readonly heights: Readonly<Record<string, CarrierHeight | null>>;
  readonly running: boolean;
  /** 走っている AGV。 */
  readonly travelingAgvs: readonly string[];
  readonly permissions: Permissions;
  readonly onSaveDraft: (draft: MapDraft) => void;
  /** 検証する（サーバーが確かめる）。見つかった問題を返す。 */
  readonly onValidate: (draft: MapDraft) => Promise<readonly string[]>;
  /** 公開する（確かめる画面で選んだあと）。 */
  readonly onPublish: (draft: MapDraft) => void;
  /** キャリアの高さを保存する（null で設定なしにする）。すぐ使われる。 */
  readonly onSaveHeight: (panel: string, heightMm: number | null) => void;
  /** 保存していない変更があるかを知らせる（ほかの画面へ移るときに確かめるため）。 */
  readonly onDirtyChange: (dirty: boolean) => void;
}

type Tool = 'select' | PanelType | 'connect' | 'erase';

const TOOLS: readonly (readonly [Tool, string])[] = [
  ['select', '選ぶ'],
  ['Normal', panelTypeLabel.Normal],
  ['Transfer', panelTypeLabel.Transfer],
  ['Standby', panelTypeLabel.Standby],
  ['SpecialTransfer', panelTypeLabel.SpecialTransfer],
  ['connect', 'つなぐ'],
  ['erase', '消す'],
];

const TOOL_HINT: Record<Tool, string> = {
  select: 'パネルを押すと、右の欄で直せます。',
  Normal: '空いた格子を押すと、通常パネルを置きます。',
  Transfer: '空いた格子を押すと、受け渡しパネルを置きます。',
  Standby: '空いた格子を押すと、待機パネルを置きます。',
  SpecialTransfer: '空いた格子を押すと、特殊消耗品受け渡しパネルを置きます。',
  connect: 'つなぐ2枚のパネルを、順に押します（上下左右の隣だけ）。',
  erase: 'パネルかつながりを押すと、消します。',
};

const CELL_PX = 72;
const INTEGER = /^-?\d+$/;

/** マップの編集（docs/screens.md の「6 マップの編集」）。パネルとつながりを置いて直し、下書きを保存し、検証してから公開する。 */
export function MapEditPage(props: MapEditPageProps) {
  const { saved, permissions } = props;
  const allowed = permissions.has('configure');
  const [draft, setDraft] = useState<MapDraft>(saved);
  const [baseline, setBaseline] = useState<MapDraft>(saved);
  const [tool, setTool] = useState<Tool>('select');
  const [selected, setSelected] = useState<PanelId | null>(null);
  const [pending, setPending] = useState<PanelId | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [validation, setValidation] = useState<{ readonly draft: MapDraft; readonly problems: readonly string[] } | null>(null);
  const [confirming, setConfirming] = useState(false);
  const dirty = isDraftChanged(baseline, draft);
  const { onDirtyChange } = props;

  useEffect(() => {
    onDirtyChange(dirty);
  }, [dirty, onDirtyChange]);

  useEffect(() => {
    if (!dirty) {
      return undefined;
    }
    const warn = (e: BeforeUnloadEvent) => e.preventDefault();
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [dirty]);

  const layout = draft.layout;
  const size = layout.panelSizeMm;
  const b = editBounds(layout.panels);
  const cols = b.maxColumn - b.minColumn + 1;
  const rows = b.maxRow - b.minRow + 1;
  const viewBox = { x: b.minColumn * size, y: b.minRow * size, width: cols * size, height: rows * size };
  const equipmentOnMap = props.equipment
    .filter((e) => draft.outlines[e.id] !== undefined && layout.panels.some((p) => p.id === e.panel))
    .map((e) => ({ id: equipmentId(e.id), kind: 'device' as const, panel: e.panel as PanelId, outlineMm: draft.outlines[e.id]!, attentions: [] }));
  const publishBlocked = publishBlockedReason({
    validated: validation?.draft ?? null,
    problems: validation?.problems ?? [],
    current: draft,
    running: props.running,
    travelingAgvs: props.travelingAgvs,
    allowed,
  });

  const pickTool = (t: Tool) => {
    setTool(t);
    setPending(null);
    setMessage(null);
  };

  const pressCell = (column: number, row: number) => {
    if (!allowed) {
      return;
    }
    if (tool === 'Normal' || tool === 'Transfer' || tool === 'Standby' || tool === 'SpecialTransfer') {
      const next = placePanel(draft, tool, column, row);
      setDraft(next);
      setSelected(next.layout.panels.at(-1)?.id ?? null);
      return;
    }
    setSelected(null);
  };

  const pressPanel = (id: PanelId) => {
    setMessage(null);
    if (!allowed || tool === 'select' || tool === 'Normal' || tool === 'Transfer' || tool === 'Standby' || tool === 'SpecialTransfer') {
      setSelected(id);
      return;
    }
    if (tool === 'erase') {
      setDraft(removePanel(draft, id));
      setSelected((s) => (s === id ? null : s));
      return;
    }
    if (pending === null) {
      setPending(id);
      return;
    }
    const result = connect(draft, pending, id);
    if (result.kind === 'refused') {
      setMessage(result.reason);
    } else {
      setDraft(result.draft);
    }
    setPending(null);
  };

  const panel = layout.panels.find((p) => p.id === selected) ?? null;

  return (
    <>
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <p className="m-0 text-sm text-ink-muted">
          下書き：版{props.draftVersion}（{props.savedAt === null ? 'まだ保存していません' : `${dateTimeOf(props.savedAt)} に保存`}
          {dirty ? '。保存していない変更があります' : ''}）　公開中：版{props.publishedVersion}
        </p>
        <div className="ml-auto flex flex-wrap gap-2">
          <button
            type="button"
            disabled={!allowed || !dirty}
            onClick={() => {
              props.onSaveDraft(draft);
              setBaseline(draft);
            }}
            className={plainButton}
          >
            下書きを保存する
          </button>
          <button
            type="button"
            disabled={!allowed}
            onClick={() => {
              const checked = draft;
              void props.onValidate(checked).then((problems) => setValidation({ draft: checked, problems }));
            }}
            className={plainButton}
          >
            確かめる（検証）
          </button>
          <button type="button" disabled={publishBlocked !== null} onClick={() => setConfirming(true)} className={darkButton}>
            版{props.draftVersion}を公開する
          </button>
        </div>
      </div>
      {publishBlocked !== null && validation !== null && <p className={`${note} -mt-2 text-right`}>{publishBlocked}</p>}
      {!allowed && <p className="m-0 text-sm font-bold">{publishBlocked}</p>}

      <div className="grid grid-cols-1 items-start gap-4 lg:grid-cols-[180px_minmax(0,1fr)_320px]">
        <nav aria-label="置くもの" className={`${section} gap-2`}>
          <h2 className="m-0 text-sm font-bold text-ink-muted">置くもの</h2>
          {TOOLS.map(([key, label]) => (
            <button
              key={key}
              type="button"
              data-tool={key}
              aria-pressed={tool === key}
              disabled={!allowed && key !== 'select'}
              onClick={() => pickTool(key)}
              className={`min-h-11 rounded-lg px-3 text-left text-sm font-bold focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-focus disabled:cursor-not-allowed disabled:text-disabled-text ${tool === key ? 'bg-graphite text-white' : 'border border-control-border bg-white text-graphite'}`}
            >
              {label}
            </button>
          ))}
        </nav>

        <section aria-label="マップ" className={`${section} gap-2`}>
          <p className={note}>
            {pending !== null ? `${pending} とつなぐパネルを押してください。` : TOOL_HINT[tool]}
          </p>
          {message !== null && (
            <p role="status" className="m-0 text-sm font-bold text-attention">
              {message}
            </p>
          )}
          <div className="overflow-auto rounded-lg bg-floor">
            <div className="relative" style={{ width: cols * CELL_PX, height: rows * CELL_PX }}>
              <MapCanvas layout={layout} viewBox={viewBox} equipment={equipmentOnMap} />
              <svg viewBox={`${viewBox.x} ${viewBox.y} ${viewBox.width} ${viewBox.height}`} className="absolute inset-0 block h-full w-full">
                {Array.from({ length: rows }, (_, r) =>
                  Array.from({ length: cols }, (_, c) => {
                    const column = b.minColumn + c;
                    const row = b.minRow + r;
                    if (layout.panels.some((p) => p.column === column && p.row === row)) {
                      return null;
                    }
                    return (
                      <rect
                        key={`${column},${row}`}
                        data-cell={`${column},${row}`}
                        x={column * size + size * 0.04}
                        y={row * size + size * 0.04}
                        width={size * 0.92}
                        height={size * 0.92}
                        onClick={() => pressCell(column, row)}
                        className="fill-white/15 stroke-control-border hover:fill-white/40"
                        strokeDasharray={`${size * 0.06} ${size * 0.04}`}
                        strokeWidth={size * 0.015}
                      />
                    );
                  }),
                )}
                {layout.connections.map((c) => {
                  const from = layout.panels.find((p) => p.id === c.from);
                  const to = layout.panels.find((p) => p.id === c.to);
                  if (from === undefined || to === undefined) {
                    return null;
                  }
                  const a = crossPoint(from, size);
                  const z = crossPoint(to, size);
                  return (
                    <line
                      key={`${c.from}-${c.to}`}
                      data-connection={`${c.from}-${c.to}`}
                      x1={a.x}
                      y1={a.y}
                      x2={z.x}
                      y2={z.y}
                      strokeWidth={size * 0.12}
                      onClick={() => {
                        if (allowed && tool === 'erase') {
                          setDraft(removeConnection(draft, c));
                        }
                      }}
                      className={`stroke-transparent ${tool === 'erase' ? 'cursor-pointer hover:stroke-attention/60' : 'pointer-events-none'}`}
                    />
                  );
                })}
                {layout.panels.map((p) => (
                  <g key={p.id}>
                    <rect
                      data-edit-panel={p.id}
                      x={p.column * size}
                      y={p.row * size}
                      width={size}
                      height={size}
                      onClick={() => pressPanel(p.id)}
                      className={`cursor-pointer fill-transparent ${p.id === selected || p.id === pending ? 'stroke-selection' : 'stroke-transparent hover:stroke-selection/60'}`}
                      strokeWidth={size * 0.06}
                    />
                    <text x={p.column * size + size * 0.06} y={p.row * size + size * 0.18} fontSize={size * 0.14} className="pointer-events-none fill-white font-number font-bold">
                      {p.id}
                    </text>
                  </g>
                ))}
              </svg>
            </div>
          </div>
        </section>

        <aside aria-labelledby="prop-title" className={`${section} gap-4`}>
          {panel === null ? (
            <MapSettings draft={draft} allowed={allowed} onChange={setDraft} />
          ) : (
            <PanelEditor
              key={panel.id}
              draft={draft}
              panelId={panel.id}
              allowed={allowed}
              equipment={props.equipment}
              height={props.heights[panel.id]}
              onChange={setDraft}
              onRenamed={setSelected}
              onSaveHeight={props.onSaveHeight}
            />
          )}
          {validation !== null && validation.draft === draft && (
            <div role={validation.problems.length > 0 ? 'alert' : 'status'} className={`rounded-lg border p-3 ${validation.problems.length > 0 ? 'border-attention bg-white' : 'border-running bg-white'}`}>
              {validation.problems.length === 0 ? (
                <p className="m-0 text-sm font-bold text-running">検証で見つかった問題はありません。公開できます。</p>
              ) : (
                <>
                  <p className="m-0 text-sm font-bold text-attention">検証で見つかった問題 {validation.problems.length}件</p>
                  <ul className="m-0 mt-1 flex flex-col gap-1 pl-5 text-[13px]">
                    {validation.problems.map((p) => (
                      <li key={p}>{p}</li>
                    ))}
                  </ul>
                </>
              )}
            </div>
          )}
        </aside>
      </div>

      {confirming && (
        <ConfirmDialog
          title={`版${props.draftVersion}を公開しますか？`}
          body="公開すると、すぐにこの版で AGV を動かします。公開した版は、元に戻せません（前の版に戻すときは、前の版を写して直し、もう一度公開します）。"
          confirmLabel={`版${props.draftVersion}を公開する`}
          tone="primary"
          onConfirm={() => {
            props.onPublish(draft);
            setBaseline(draft);
            setConfirming(false);
          }}
          onCancel={() => setConfirming(false)}
        />
      )}
    </>
  );
}

function MapSettings({ draft, allowed, onChange }: { readonly draft: MapDraft; readonly allowed: boolean; readonly onChange: (d: MapDraft) => void }) {
  const [size, setSize] = useState(String(draft.layout.panelSizeMm));
  const ok = /^\d+$/.test(size.trim()) && Number(size) >= 1;
  return (
    <>
      <h2 id="prop-title" className="m-0 text-lg font-bold">
        マップ全体
      </h2>
      <p className={note}>パネルを押すと、そのパネルを直せます。</p>
      <div className="flex items-end gap-2">
        <div className="flex-1">
          <TextField label="パネルの一辺" unit="mm" numeric value={size} onChange={setSize} />
        </div>
        <button
          type="button"
          disabled={!allowed || !ok || Number(size) === draft.layout.panelSizeMm}
          onClick={() => onChange({ ...draft, layout: { ...draft.layout, panelSizeMm: Number(size) } })}
          className={plainButton}
        >
          当てる
        </button>
      </div>
      {!ok && <p className={note}>パネルの一辺は、1 以上の整数（mm）で入れてください。</p>}
    </>
  );
}

/** 整数を入れて「当てる」と、下書きに入れる欄。 */
function IntInput({
  label,
  unit,
  initial,
  min,
  max,
  allowEmpty = false,
  disabled,
  onApply,
}: {
  readonly label: string;
  readonly unit?: string | undefined;
  readonly initial: number | null;
  readonly min?: number;
  readonly max?: number;
  readonly allowEmpty?: boolean;
  readonly disabled: boolean;
  readonly onApply: (value: number | null) => void;
}) {
  const [text, setText] = useState(initial === null ? '' : String(initial));
  const empty = text.trim() === '';
  const value = Number(text);
  const ok = empty ? allowEmpty : INTEGER.test(text.trim()) && (min === undefined || value >= min) && (max === undefined || value <= max);
  const same = empty ? initial === null : value === initial;
  const range = min !== undefined && max !== undefined ? `${min}〜${max} の整数` : min !== undefined ? `${min} 以上の整数` : '整数';
  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-end gap-2">
        <div className="flex-1">
          <TextField label={label} unit={unit} numeric value={text} onChange={setText} />
        </div>
        <button type="button" disabled={disabled || !ok || same} onClick={() => onApply(empty ? null : value)} className={plainButton}>
          当てる
        </button>
      </div>
      {!ok && <p className={note}>{`${label}は、${range}で入れてください。`}</p>}
    </div>
  );
}

function PanelEditor({
  draft,
  panelId: id,
  allowed,
  equipment,
  height,
  onChange,
  onRenamed,
  onSaveHeight,
}: {
  readonly draft: MapDraft;
  readonly panelId: PanelId;
  readonly allowed: boolean;
  readonly equipment: readonly { readonly id: string; readonly panel: string }[];
  readonly height: CarrierHeight | null | undefined;
  readonly onChange: (d: MapDraft) => void;
  readonly onRenamed: (id: PanelId) => void;
  readonly onSaveHeight: (panel: string, heightMm: number | null) => void;
}) {
  const panel = draft.layout.panels.find((p) => p.id === id);
  const [name, setName] = useState<string>(id);
  const [renameError, setRenameError] = useState<string | null>(null);
  const [heightText, setHeightText] = useState(height ? String(height.heightMm) : '');
  if (panel === undefined) {
    return null;
  }
  const partner = equipment.find((e) => e.panel === id);
  const pinAllowed = panel.type === 'Transfer' || panel.type === 'Standby';
  const deadEnd = pinAllowed;
  const links = draft.layout.connections.filter((c) => c.from === id || c.to === id);
  const heightOk = heightText.trim() === '' || (/^\d+$/.test(heightText.trim()) && Number(heightText) >= 1);

  return (
    <>
      <h2 id="prop-title" className="m-0 text-lg font-bold">
        選んだパネル：<span className="font-number text-xl">{id}</span>
      </h2>
      <div className="flex flex-col gap-1">
        <div className="flex items-end gap-2">
          <div className="flex-1">
            <TextField label="パネルの ID" value={name} onChange={setName} />
          </div>
          <button
            type="button"
            disabled={!allowed || name.trim() === id}
            onClick={() => {
              const r = renamePanel(
                draft,
                id,
                name,
                equipment.map((e) => e.panel),
              );
              if (r.kind === 'refused') {
                setRenameError(r.reason);
              } else {
                onChange(r.draft);
                onRenamed(name.trim() as PanelId);
              }
            }}
            className={plainButton}
          >
            ID を直す
          </button>
        </div>
        {renameError !== null && <p className="m-0 text-[13px] font-bold text-attention">{renameError}</p>}
      </div>
      <label className="flex flex-col gap-1.5 text-sm font-bold">
        種類
        <select aria-label="種類" value={panel.type} disabled={!allowed} onChange={(e) => onChange(setPanelType(draft, id, e.target.value as PanelType))} className={field}>
          {(Object.keys(panelTypeLabel) as PanelType[]).map((t) => (
            <option key={t} value={t}>
              {panelTypeLabel[t]}
            </option>
          ))}
        </select>
      </label>
      {(panel.type === 'Transfer' || panel.type === 'SpecialTransfer') && <p className="m-0 text-sm">受け渡しの相手：{partner?.id ?? 'なし（消耗品の構成で決めます）'}</p>}
      <label className="flex min-h-11 items-center gap-2 text-[15px]">
        <input
          type="checkbox"
          checked={panel.hasChargingPin}
          disabled={!allowed || !pinAllowed}
          onChange={(e) => onChange(updatePanel(draft, id, (p) => ({ ...p, hasChargingPin: e.target.checked })))}
          className="size-5 accent-graphite"
        />
        充電ピンあり{pinAllowed ? '' : '（このパネルには付けられません）'}
      </label>
      {panel.type === 'SpecialTransfer' && (
        <IntInput
          label="キャリアを下ろす角度"
          unit="北が0度、時計回り"
          initial={panel.carrierLoweringHeading ?? null}
          min={0}
          max={359}
          allowEmpty
          disabled={!allowed}
          onApply={(v) => onChange(setLoweringHeading(draft, id, v === null ? null : heading(v)))}
        />
      )}
      <div className="grid grid-cols-2 gap-2">
        <IntInput
          label="十字のずれ（東）"
          unit="mm"
          initial={panel.crossOffsetMm.x}
          disabled={!allowed}
          onApply={(v) => onChange(updatePanel(draft, id, (p) => ({ ...p, crossOffsetMm: { ...p.crossOffsetMm, x: v ?? 0 } })))}
        />
        <IntInput
          label="十字のずれ（南）"
          unit="mm"
          initial={panel.crossOffsetMm.y}
          disabled={!allowed}
          onApply={(v) => onChange(updatePanel(draft, id, (p) => ({ ...p, crossOffsetMm: { ...p.crossOffsetMm, y: v ?? 0 } })))}
        />
      </div>

      <div className="flex flex-col gap-2">
        <h3 className="m-0 text-sm font-bold text-ink-muted">つながり</h3>
        {links.length === 0 && <p className={note}>つながりはありません。「つなぐ」で隣のパネルとつなぎます。</p>}
        {links.map((c) => {
          const view = connectionView(c, id);
          const fixedTwoWay = deadEnd || [c.from, c.to].some((x) => {
            const t = draft.layout.panels.find((p) => p.id === x)?.type;
            return t === 'Transfer' || t === 'Standby';
          });
          return (
            <div key={`${c.from}-${c.to}`} className="flex flex-col gap-2 rounded-lg border border-line bg-white p-2.5">
              <div className="flex items-center justify-between gap-2">
                <span className="font-bold">{view.other} へ</span>
                <button type="button" disabled={!allowed} onClick={() => onChange(removeConnection(draft, c))} className={plainButton}>
                  外す
                </button>
              </div>
              <IntInput label={`${view.other} までの距離`} unit="mm" initial={c.distanceMm} min={1} disabled={!allowed} onApply={(v) => onChange(setConnection(draft, c, id, { distanceMm: v ?? c.distanceMm }))} />
              <IntInput label={`${view.other} へ進む向き`} unit="度" initial={view.heading} min={0} max={359} disabled={!allowed} onApply={(v) => onChange(setConnection(draft, c, id, { heading: v ?? view.heading }))} />
              <label className="flex min-h-11 items-center gap-2 text-[14px]">
                <input
                  type="checkbox"
                  checked={c.oneWay}
                  disabled={!allowed || fixedTwoWay}
                  onChange={(e) => onChange(setConnection(draft, c, id, { oneWay: e.target.checked }))}
                  className="size-5 accent-graphite"
                />
                一方通行（{c.from} から {c.to} へだけ）{fixedTwoWay ? '：受け渡し・待機パネルへのつながりは、一方通行にできません' : ''}
              </label>
            </div>
          );
        })}
      </div>

      {partner !== undefined && <OutlineEditor key={partner.id} draft={draft} equipment={partner.id} allowed={allowed} onChange={onChange} />}

      {height !== undefined && (
        <div className="flex flex-col gap-2 border-t border-line pt-3">
          <h3 className="m-0 text-sm font-bold text-ink-muted">キャリアの高さ（下書きとは別に、保存するとすぐ使います）</h3>
          <p className={note}>
            {height === null
              ? '設定なし（AGV が測ります）'
              : `${height.heightMm} mm（${height.source === 'measured' ? 'AGV が測った値' : '手入力した値'}、${dateTimeOf(height.at)}）`}
          </p>
          <div className="flex items-end gap-2">
            <div className="flex-1">
              <TextField label="キャリアの高さ" unit="mm" numeric placeholder="空なら AGV が測る" value={heightText} onChange={setHeightText} />
            </div>
            <button
              type="button"
              disabled={!allowed || !heightOk}
              onClick={() => onSaveHeight(id, heightText.trim() === '' ? null : Number(heightText))}
              className={plainButton}
            >
              高さを保存する
            </button>
          </div>
          {!heightOk && <p className={note}>キャリアの高さは、1 以上の整数（mm）で入れてください。</p>}
        </div>
      )}
    </>
  );
}

function OutlineEditor({ draft, equipment, allowed, onChange }: { readonly draft: MapDraft; readonly equipment: string; readonly allowed: boolean; readonly onChange: (d: MapDraft) => void }) {
  const current = draft.outlines[equipment];
  const [text, setText] = useState(() => ({
    x: current ? String(current.x) : '',
    y: current ? String(current.y) : '',
    width: current ? String(current.width) : '',
    height: current ? String(current.height) : '',
  }));
  const ok = INTEGER.test(text.x.trim()) && INTEGER.test(text.y.trim()) && /^\d+$/.test(text.width.trim()) && Number(text.width) >= 1 && /^\d+$/.test(text.height.trim()) && Number(text.height) >= 1;
  const fields: readonly (readonly [keyof typeof text, string])[] = [
    ['x', '左（東へ）'],
    ['y', '上（南へ）'],
    ['width', '幅'],
    ['height', '奥行き'],
  ];
  return (
    <div className="flex flex-col gap-2 border-t border-line pt-3">
      <h3 className="m-0 text-sm font-bold text-ink-muted">{equipment} の外形（mm。マップの左上が 0）</h3>
      <div className="grid grid-cols-2 gap-2">
        {fields.map(([key, label]) => (
          <TextField key={key} label={`外形の${label}`} numeric value={text[key]} onChange={(v) => setText((t) => ({ ...t, [key]: v }))} />
        ))}
      </div>
      {!ok && <p className={note}>左と上は整数、幅と奥行きは 1 以上の整数で、すべて入れてください。</p>}
      <div>
        <button
          type="button"
          disabled={!allowed || !ok}
          onClick={() => {
            const outline: Rect = { x: Number(text.x), y: Number(text.y), width: Number(text.width), height: Number(text.height) };
            onChange({ ...draft, outlines: { ...draft.outlines, [equipment]: outline } });
          }}
          className={plainButton}
        >
          外形を当てる
        </button>
      </div>
    </div>
  );
}
