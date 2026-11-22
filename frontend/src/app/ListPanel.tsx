import { useId, useState } from 'react';
import { indicatorLight, lightLabel, type AgvId } from '../map/agv';
import { attentionTag, type EquipmentId } from '../map/equipment';
import type { PanelId } from '../map/model';
import type { AgvDetail } from './AgvCard';
import { controlStatusLabel } from './controlStatus';
import type { EquipmentDetail } from './EquipmentCard';
import { byIdNumber, matchesQuery, needsAttention, sortForList } from './listing';

/** 一覧で選ぶもの。 */
export type ListItemRef =
  | { readonly kind: 'agv'; readonly id: AgvId }
  | { readonly kind: 'equipment'; readonly id: EquipmentId }
  | { readonly kind: 'consumable'; readonly id: string };

/** 探した消耗品。ある場所（AGV か、装置かステーション）をマップで示す。 */
export interface ConsumableHit {
  readonly id: string;
  readonly variety: string;
  readonly condition: string;
  /** 所在（画面に出す文）。 */
  readonly where: string;
  readonly status: string;
  readonly at: { readonly kind: 'agv'; readonly id: AgvId } | { readonly kind: 'equipment'; readonly id: EquipmentId };
}

export interface ListPanelProps {
  readonly agvs: readonly AgvDetail[];
  readonly equipment: readonly EquipmentDetail[];
  /** 消耗品を ID で探す。 */
  readonly searchConsumables: (query: string) => readonly ConsumableHit[];
  /** いるパネルに充電ピンがあるか（標示灯の色を決めるため）。 */
  readonly isChargingPanel?: (panel: PanelId) => boolean;
  readonly onSelect: (item: ListItemRef) => void;
  /** マウスを乗せた行を知らせる（外したら null）。 */
  readonly onHighlight: (item: ListItemRef | null) => void;
  readonly onClose: () => void;
}

type Tab = 'agv' | 'device' | 'station' | 'consumable';

const TABS: readonly (readonly [Tab, string, string])[] = [
  ['agv', 'AGV', 'AGV の番号で探す'],
  ['device', '装置', '装置の名前で探す'],
  ['station', 'ステーション', 'ステーションの名前で探す'],
  ['consumable', '消耗品', '消耗品の ID で探す'],
];

const LIGHT_BG = {
  moving: 'bg-light-moving',
  waiting: 'bg-light-waiting',
  charging: 'bg-light-charging',
  chargeFailed: 'bg-light-charge-failed',
} as const;

interface Row {
  readonly key: string;
  readonly ref: ListItemRef;
  readonly title: string;
  readonly sub: string;
  readonly right: string;
  readonly dot: string;
}

/** 一覧（左に出す）。AGV・装置・ステーション・消耗品を探して選ぶ。 */
export function ListPanel({ agvs, equipment, searchConsumables, isChargingPanel = () => false, onSelect, onHighlight, onClose }: ListPanelProps) {
  const searchId = useId();
  const [tab, setTab] = useState<Tab>('agv');
  const [queries, setQueries] = useState<Readonly<Record<Tab, string>>>({ agv: '', device: '', station: '', consumable: '' });
  const query = queries[tab];
  const label = TABS.find((t) => t[0] === tab)?.[2] ?? '';

  const rows: Row[] = (() => {
    switch (tab) {
      case 'agv':
        return sortForList(agvs, (a) => needsAttention(a.operation))
          .filter((a) => matchesQuery(a.id, query))
          .map((a) => {
            const light = indicatorLight(a.status, isChargingPanel(a.panel));
            return {
              key: a.id,
              ref: { kind: 'agv', id: a.id },
              title: a.id,
              sub: `${lightLabel(light.color)}（${controlStatusLabel[a.control]}）`,
              right: a.batteryPercent === null ? '不明' : `${a.batteryPercent}%`,
              dot: `ring-1 ring-graphite ${LIGHT_BG[light.color]}`,
            };
          });
      case 'device':
      case 'station':
        return sortForList(
          equipment.filter((e) => e.kind === tab),
          (e) => e.attentions.length > 0,
        )
          .filter((e) => matchesQuery(e.id, query))
          .map((e) => ({
            key: e.id,
            ref: { kind: 'equipment', id: e.id },
            title: e.id,
            sub: e.attentions.length > 0 ? e.attentions.map(attentionTag).join('、') : '気をつけることはありません',
            right: `パネル ${e.panel}`,
            dot: e.attentions.length > 0 ? 'bg-attention' : 'ring-1 ring-control-border ring-inset',
          }));
      case 'consumable':
        return query.trim() === ''
          ? []
          : [...searchConsumables(query.trim())]
              .sort((a, b) => byIdNumber(a.id, b.id))
              .map((c) => ({
                key: c.id,
                ref: { kind: 'consumable', id: c.id },
                title: c.id,
                sub: `${c.variety}（${c.where}）`,
                right: c.condition,
                dot: 'invisible',
              }));
    }
  })();

  const emptyText =
    tab === 'consumable' && query.trim() === ''
      ? '消耗品の ID を入れると、ある場所をマップで示します。'
      : `「${query}」に合うものはありません。${tab === 'consumable' ? 'ID' : tab === 'agv' ? '番号' : '名前'}を確かめてください。`;

  return (
    <section aria-labelledby="list-title" className="flex flex-col bg-paper text-graphite">
      <div className="sticky top-0 z-1 bg-paper">
        <div className="flex items-center justify-between gap-2 bg-graphite px-5 py-4 text-white">
          <h2 id="list-title" className="m-0 text-xl leading-tight font-bold">
            一覧
          </h2>
          <button
            type="button"
            aria-label="閉じる"
            onClick={onClose}
            className="flex size-11 items-center justify-center rounded-lg border border-control-border text-white focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-focus"
          >
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
              <path d="M18 6 6 18" />
              <path d="m6 6 12 12" />
            </svg>
          </button>
        </div>
        <div role="tablist" aria-label="一覧の種類" className="grid grid-cols-4 border-b border-line">
          {TABS.map(([key, name]) => (
            <button
              key={key}
              type="button"
              role="tab"
              aria-selected={tab === key}
              onClick={() => {
                setTab(key);
                onHighlight(null);
              }}
              className={`min-h-12 border-b-3 px-1 text-[13px] font-bold focus-visible:outline-3 focus-visible:outline-focus ${tab === key ? 'border-graphite text-graphite' : 'border-transparent text-ink-muted'}`}
            >
              {name}
            </button>
          ))}
        </div>
        <div className="border-b border-line px-5 py-3">
          <label htmlFor={searchId} className="sr-only">
            {label}
          </label>
          <input
            id={searchId}
            type="search"
            value={query}
            placeholder={label}
            onChange={(e) => setQueries((q) => ({ ...q, [tab]: e.target.value }))}
            className="min-h-11 w-full rounded-lg border border-control-border bg-white px-3 text-[15px] text-graphite focus-visible:outline-3 focus-visible:outline-focus"
          />
        </div>
      </div>
      <ul className="m-0 flex list-none flex-col gap-1.5 px-3 pt-2.5 pb-5">
        {rows.map((r) => (
          <li key={r.key}>
            <button
              type="button"
              data-row={r.key}
              onClick={() => onSelect(r.ref)}
              onMouseEnter={() => onHighlight(r.ref)}
              onMouseLeave={() => onHighlight(null)}
              onFocus={() => onHighlight(r.ref)}
              onBlur={() => onHighlight(null)}
              className="grid min-h-15 w-full grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-x-2.5 rounded-lg border border-line bg-white px-3 py-2 text-left text-graphite hover:border-selection hover:ring-2 hover:ring-selection focus-visible:border-selection focus-visible:ring-2 focus-visible:ring-selection focus-visible:outline-none"
            >
              <span aria-hidden="true" className={`row-span-2 size-3.5 rounded-full ${r.dot}`} />
              <span className="font-number text-[19px] leading-tight font-bold">{r.title}</span>
              <span className="row-span-2 font-number text-[15px] font-semibold">{r.right}</span>
              <span className="text-[13px] text-ink-muted">{r.sub}</span>
            </button>
          </li>
        ))}
      </ul>
      {rows.length === 0 && <p className="m-0 px-5 pt-1 pb-5 text-sm text-ink-muted">{emptyText}</p>}
    </section>
  );
}
