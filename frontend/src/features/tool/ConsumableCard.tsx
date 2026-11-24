import { CardHeader } from '../../shared/components/CardHeader';
import type { ConsumableHit } from '../routing/ListPanel';

export interface ConsumableCardProps {
  readonly consumable: ConsumableHit;
  readonly onClose: () => void;
  readonly onBack?: (() => void) | undefined;
  /** 「消耗品の検索と手入力」の画面で、この消耗品を開く。 */
  readonly onOpen: (id: string) => void;
}

/** 消耗品のカード。所在と状態だけを出し、手入力と履歴は詳しい画面で行う。 */
export function ConsumableCard({ consumable: c, onClose, onBack, onOpen }: ConsumableCardProps) {
  return (
    <section aria-labelledby={`consumable-${c.id}`} className="flex flex-col bg-paper text-graphite">
      <div className="sticky top-0 z-1">
        <CardHeader titleId={`consumable-${c.id}`} title={c.id} subtitle="消耗品" onClose={onClose} onBack={onBack} />
      </div>
      <dl className="m-0 grid grid-cols-[max-content_1fr] gap-x-4 gap-y-2 px-5 py-4 text-sm">
        <dt className="text-ink-muted">所在</dt>
        <dd className="m-0 font-bold">{c.where}</dd>
        <dt className="text-ink-muted">品種</dt>
        <dd className="m-0">{c.variety}</dd>
        <dt className="text-ink-muted">新品・中古品</dt>
        <dd className="m-0">{c.condition}</dd>
        <dt className="text-ink-muted">状態</dt>
        <dd className="m-0">{c.status}</dd>
      </dl>
      <p className="m-0 px-5 pb-4 text-[13px] text-ink-muted">ある場所を、マップの上で水色の点線で示しています。</p>
      <div className="border-t border-line px-5 pt-4 pb-6">
        <button
          type="button"
          onClick={() => onOpen(c.id)}
          className="flex min-h-11 w-full items-center justify-center rounded-lg bg-graphite px-3.5 text-sm font-bold text-white focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-focus"
        >
          これまでの動きと手入力を開く
        </button>
      </div>
    </section>
  );
}
