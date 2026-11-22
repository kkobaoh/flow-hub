const iconButton =
  'flex size-11 shrink-0 items-center justify-center rounded-lg border border-control-border text-white focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-focus';

export interface CardHeaderProps {
  readonly titleId: string;
  readonly title: string;
  readonly subtitle?: string;
  /** 見出しを番号の字（AGV-1 など）で書くか。 */
  readonly numeric?: boolean;
  readonly onClose: () => void;
  /** 一覧から開いたときだけ、一覧に戻るボタンを出す。 */
  readonly onBack?: (() => void) | undefined;
}

/** 左から出すカードの見出し（一覧に戻る、名前、閉じる）。カードの種類によらず同じ形にする。 */
export function CardHeader({ titleId, title, subtitle, numeric = true, onClose, onBack }: CardHeaderProps) {
  return (
    <div className="flex items-center justify-between gap-2 bg-graphite px-5 py-4 text-white">
      <div className="flex min-w-0 items-center gap-2.5">
        {onBack && (
          <button type="button" aria-label="一覧に戻る" title="一覧に戻る" onClick={onBack} className={iconButton}>
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <path d="m15 6-6 6 6 6" />
            </svg>
          </button>
        )}
        <div className="flex min-w-0 flex-col gap-1">
          <h2 id={titleId} className={numeric ? 'm-0 font-number text-3xl leading-none font-bold' : 'm-0 text-xl leading-tight font-bold'}>
            {title}
          </h2>
          {subtitle && <span className="text-[13px] text-header-muted">{subtitle}</span>}
        </div>
      </div>
      <button type="button" aria-label="閉じる" onClick={onClose} className={iconButton}>
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
          <path d="M18 6 6 18" />
          <path d="m6 6 12 12" />
        </svg>
      </button>
    </div>
  );
}
