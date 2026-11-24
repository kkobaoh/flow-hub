import { attentionNote, type AttentionKind, type EquipmentId, type EquipmentOnMap } from './equipment';
import { CardHeader } from '../../shared/components/CardHeader';
import type { Permissions } from '../auth/permissions';

interface EquipmentBase extends Omit<EquipmentOnMap, 'kind'> {
  /** 気をつけることの中身（種類ごとに、サーバーが知らせる文）。 */
  readonly details: Readonly<Partial<Record<AttentionKind, string>>>;
}

export interface StockerRow {
  readonly variety: string;
  /** 設定の個数。 */
  readonly target: number;
  /** ある新品（届ける途中を含む）。 */
  readonly have: number;
}

export interface StockRow {
  readonly variety: string;
  /** 届けられる新品。 */
  readonly available: number;
  /** 補充を頼んで、まだ入っていない数。 */
  readonly replenishWaiting: number;
}

/** カードに出す装置かステーションの詳細。 */
export type EquipmentDetail =
  | (EquipmentBase & {
      readonly kind: 'device';
      readonly stocker: readonly StockerRow[];
      readonly slots: { readonly total: number; readonly free: number; readonly delivering: number };
    })
  | (EquipmentBase & { readonly kind: 'station'; readonly stock: readonly StockRow[] });

export interface EquipmentCardProps {
  readonly equipment: EquipmentDetail;
  readonly permissions: Permissions;
  readonly onClose: () => void;
  /** その装置やステーションを、詳しい画面で開く。 */
  readonly onOpen: (screen: 'devices' | 'station', id: EquipmentId) => void;
  /** 一覧から開いたときだけ渡す（一覧に戻るボタンを出す）。 */
  readonly onBack?: (() => void) | undefined;
}

const action =
  'flex min-h-11 items-center justify-center rounded-lg px-3.5 text-sm font-bold focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-focus disabled:cursor-not-allowed disabled:bg-ground disabled:text-disabled-text';
const th = 'border-b border-line px-1 py-1.5 text-left font-bold text-ink-muted';

/** 装置かステーションのカード。気をつけることと数を出し、詳しい画面への入り口を置く。 */
export function EquipmentCard({ equipment: e, permissions, onClose, onOpen, onBack }: EquipmentCardProps) {
  const device = e.kind === 'device';
  const screen = device ? 'devices' : 'station';
  const primary = device
    ? { label: 'ストッカーの設定を変える（段取り替え）', allowed: permissions.has('stockerSetting') }
    : { label: '要確認の消耗品を直す', allowed: permissions.has('consumableOps') };

  return (
    <section aria-labelledby={`equipment-${e.id}`} className="flex flex-col bg-paper text-graphite">
      <div className="sticky top-0 z-1">
        <CardHeader
          titleId={`equipment-${e.id}`}
          title={e.id}
          subtitle={`${device ? '装置' : 'ステーション'}（受け渡しパネル ${e.panel}）`}
          onClose={onClose}
          onBack={onBack}
        />
      </div>

      {e.attentions.length > 0 ? (
        <div className="flex flex-col gap-2 border-b border-line px-5 py-4">
          <h3 className="m-0 text-sm font-bold">気をつけること</h3>
          {e.attentions.map((a) => (
            <div key={a.kind} data-note="" className="flex flex-col gap-0.5 rounded-lg border border-attention bg-white px-3 py-2.5">
              <div className="flex items-center gap-1.5 text-sm font-bold">
                <span aria-hidden="true" className="size-2 shrink-0 rounded-full bg-attention" />
                {attentionNote(a)}
              </div>
              <div className="text-[13px] text-ink-muted">{e.details[a.kind] ?? ''}</div>
            </div>
          ))}
        </div>
      ) : (
        <div className="flex items-center gap-2 border-b border-line px-5 py-4 text-sm font-bold text-running">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="m5 12 5 5 9-10" />
          </svg>
          気をつけることはありません
        </div>
      )}

      <div className="flex flex-col gap-2 px-5 py-4">
        <h3 className="m-0 text-sm font-bold">{device ? 'ストッカーの設定' : '品種ごとの数'}</h3>
        <table className="w-full border-collapse text-[13px]">
          <thead>
            <tr>
              <th scope="col" className={th}>品種</th>
              <th scope="col" className={th}>{device ? '設定' : '届けられる新品'}</th>
              <th scope="col" className={th}>{device ? 'ある新品' : '補充待ち'}</th>
            </tr>
          </thead>
          <tbody>
            {(e.kind === 'device'
              ? e.stocker.map((r) => [r.variety, r.target, r.have] as const)
              : e.stock.map((r) => [r.variety, r.available, r.replenishWaiting] as const)
            ).map(([variety, a, b]) => (
              <tr key={variety} className="border-b border-track">
                <th scope="row" className="px-1 py-2 text-left font-bold">{variety}</th>
                <td className="px-1 py-2 font-number text-[15px] font-semibold">{a}</td>
                <td className="px-1 py-2 font-number text-[15px] font-semibold">{b}</td>
              </tr>
            ))}
          </tbody>
        </table>
        <p className="m-0 text-[13px] text-ink-muted">
          {e.kind === 'device'
            ? `スロット ${e.slots.total}（空き ${e.slots.free}、届ける途中 ${e.slots.delivering}）。ある新品には、届ける途中のものを含みます。`
            : '届ける予約が付いている新品は、数に入れていません。'}
        </p>
      </div>

      <div className="flex flex-col gap-2 border-t border-line px-5 pt-4 pb-6">
        <button type="button" disabled={!primary.allowed} onClick={() => onOpen(screen, e.id)} className={`${action} bg-graphite text-white`}>
          {primary.label}
        </button>
        <button type="button" onClick={() => onOpen(screen, e.id)} className={`${action} border border-control-border bg-white text-graphite`}>
          {device ? '装置とストッカーを開く' : 'ステーションの在庫を開く'}
        </button>
      </div>
    </section>
  );
}
