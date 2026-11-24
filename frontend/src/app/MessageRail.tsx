import { useState } from 'react';

/** 画面で知らせるメッセージ。 */
export interface Message {
  readonly id: string;
  /** 届いた時刻（画面に出す形）。 */
  readonly time: string;
  readonly title: string;
  readonly detail: string;
  /** まだ見ていないか。 */
  readonly unread: boolean;
}

export interface MessageRailProps {
  /** 新しい順に並べたメッセージ。 */
  readonly messages: readonly Message[];
  readonly onMarkAllSeen: () => void;
  /** たたむ（スマートフォンで、下から出した列を閉じる）。 */
  readonly onClose: () => void;
}

const segment = 'min-h-11 px-3 text-[13px] font-bold focus-visible:outline-3 focus-visible:outline-focus';

/** メッセージの列。まだ見ていないものと、すべてを切り替えて見る。 */
export function MessageRail({ messages, onMarkAllSeen, onClose }: MessageRailProps) {
  const [filter, setFilter] = useState<'unread' | 'all'>('unread');
  const unread = messages.filter((m) => m.unread);
  const shown = filter === 'all' ? messages : unread;

  return (
    <section aria-labelledby="message-title" className="flex flex-col gap-1 bg-paper p-5 text-graphite">
      <div className="flex items-center justify-between gap-2 pb-2">
        <h2 id="message-title" className="m-0 text-lg font-bold">
          メッセージ
        </h2>
        <button
          type="button"
          aria-label="メッセージをたたむ"
          onClick={onClose}
          className="flex size-11 items-center justify-center rounded-lg border border-control-border bg-white text-graphite focus-visible:outline-3 focus-visible:outline-focus sm:hidden"
        >
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="m6 9 6 6 6-6" />
          </svg>
        </button>
      </div>
      <div className="flex flex-wrap items-center justify-between gap-2 pb-2">
        <div role="group" aria-label="表示するメッセージ" className="flex overflow-hidden rounded-lg border border-control-border bg-white">
          {(
            [
              ['unread', 'まだ見ていない'],
              ['all', 'すべて'],
            ] as const
          ).map(([key, label]) => (
            <button
              key={key}
              type="button"
              aria-pressed={filter === key}
              onClick={() => setFilter(key)}
              className={`${segment} ${filter === key ? 'bg-graphite text-white' : 'bg-white text-graphite'}`}
            >
              {label}
            </button>
          ))}
        </div>
        <button
          type="button"
          disabled={unread.length === 0}
          onClick={onMarkAllSeen}
          className="min-h-11 px-1 text-[13px] font-bold text-focus underline disabled:cursor-not-allowed disabled:text-disabled-text disabled:no-underline"
        >
          すべて見たことにする
        </button>
      </div>
      {shown.map((m) => (
        <div key={m.id} data-message={m.id} className="flex flex-col gap-0.5 border-t border-line py-3">
          <div className="font-number text-sm font-semibold text-ink-muted">{m.time}</div>
          <div className="flex items-center gap-1.5 text-[15px] font-bold">
            <span
              aria-label={m.unread ? 'まだ見ていない' : '見た'}
              className={`size-2 shrink-0 rounded-full ${m.unread ? 'bg-attention' : 'ring-1 ring-control-border ring-inset'}`}
            />
            {m.title}
          </div>
          <div className="text-[13px] text-ink-muted">{m.detail}</div>
        </div>
      ))}
      {shown.length === 0 && (
        <div className="flex items-center gap-2 border-t border-line py-4 text-[15px] font-bold text-running">
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="m5 12 5 5 9-10" />
          </svg>
          {filter === 'all' ? 'メッセージはありません' : 'まだ見ていないメッセージはありません'}
        </div>
      )}
    </section>
  );
}

export interface MessageTabProps {
  readonly open: boolean;
  readonly unreadCount: number;
  readonly onToggle: () => void;
}

/** メッセージの列を開け閉めするタブ（列との境目に置く）。たたんでいるときは、まだ見ていない件数を出す。 */
export function MessageTab({ open, unreadCount, onToggle }: MessageTabProps) {
  const label = open ? 'メッセージの列をたたむ' : `メッセージの列を開く（まだ見ていないもの ${unreadCount}件）`;
  return (
    <button
      type="button"
      aria-expanded={open}
      aria-controls="message-rail"
      aria-label={label}
      title={label}
      onClick={onToggle}
      className="flex min-h-11 min-w-11 items-center justify-center gap-1.5 rounded-l-lg border border-r-0 border-line bg-paper py-0 pr-3 pl-2.5 text-sm font-bold text-graphite shadow-[-2px_2px_8px_rgba(35,38,43,0.15)] focus-visible:outline-3 focus-visible:outline-focus"
    >
      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" className={open ? 'rotate-180' : ''}>
        <path d="m15 6-6 6 6 6" />
      </svg>
      {!open && (
        <>
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M21 12a8 8 0 0 1-11.6 7.1L4 20l1-4.6A8 8 0 1 1 21 12z" />
          </svg>
          {unreadCount > 0 && (
            <span data-count="" className="inline-flex h-[22px] min-w-[22px] items-center justify-center rounded-full bg-attention px-1.5 font-number text-sm font-bold text-white">
              {unreadCount}
            </span>
          )}
        </>
      )}
    </button>
  );
}
