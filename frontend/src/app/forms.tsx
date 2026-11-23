import { useEffect, useId, useRef, type ReactNode } from 'react';

/** 入力の画面（設定の画面で使う、ボタンと入力欄の見た目と、入力用のダイアログ）。 */

const buttonBase =
  'min-h-11 rounded-lg px-3.5 text-sm font-bold whitespace-nowrap focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-focus disabled:cursor-not-allowed';
export const plainButton = `${buttonBase} border border-control-border bg-white text-graphite disabled:border-disabled-border disabled:bg-ground disabled:text-disabled-text`;
export const darkButton = `${buttonBase} bg-graphite text-white disabled:bg-disabled-border disabled:text-disabled-text`;
export const field =
  'min-h-11 rounded-lg border border-control-border bg-white px-2.5 text-[15px] font-normal text-graphite focus-visible:outline-3 focus-visible:outline-focus';
export const note = 'm-0 text-[13px] text-ink-muted';
export const th = 'border-b border-line px-3 py-2 text-left font-bold whitespace-nowrap text-ink-muted';
export const td = 'px-3 py-2.5 align-top';
export const section = 'flex flex-col gap-3 rounded-[10px] border border-line bg-paper p-5';

/** 入力用のダイアログ。開いたら最初の入力欄に焦点を置き、Esc で閉じる。 */
export function FormDialog({ title, onCancel, children }: { readonly title: string; readonly onCancel: () => void; readonly children: ReactNode }) {
  const titleId = useId();
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    ref.current?.querySelector<HTMLElement>('input, select')?.focus();
  }, []);
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-graphite/55 p-4">
      <div
        ref={ref}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        onKeyDown={(e) => {
          if (e.key === 'Escape') {
            onCancel();
          }
        }}
        className="flex max-h-full w-full max-w-[520px] flex-col gap-4 overflow-y-auto rounded-xl bg-paper p-6 text-graphite"
      >
        <h2 id={titleId} className="m-0 text-xl font-bold">
          {title}
        </h2>
        {children}
      </div>
    </div>
  );
}

/** ダイアログの下（保存できない理由、やめる、保存する）。 */
export function FormFooter({
  problem,
  saveLabel,
  onCancel,
  onSave,
}: {
  readonly problem: string | null;
  readonly saveLabel: string;
  readonly onCancel: () => void;
  readonly onSave: () => void;
}) {
  return (
    <>
      {problem !== null && <p className="m-0 text-sm text-ink-muted">{problem}</p>}
      <div className="flex flex-wrap justify-end gap-2">
        <button type="button" onClick={onCancel} className={plainButton}>
          やめる
        </button>
        <button type="button" disabled={problem !== null} onClick={onSave} className={darkButton}>
          {saveLabel}
        </button>
      </div>
    </>
  );
}

/** 見出しつきの選ぶ欄。最初は「選ぶ」（未選択）。 */
export function Choice({
  label,
  value,
  options,
  onChange,
  unselected = true,
}: {
  readonly label: string;
  readonly value: string;
  readonly options: readonly (string | readonly [string, string])[];
  readonly onChange: (v: string) => void;
  /** 「選ぶ」（未選択）を置くか。 */
  readonly unselected?: boolean;
}) {
  return (
    <label className="flex flex-col gap-1.5 text-sm font-bold">
      {label}
      <select aria-label={label} value={value} onChange={(e) => onChange(e.target.value)} className={field}>
        {unselected && <option value="">選ぶ</option>}
        {options.map((o) => {
          const [v, text] = typeof o === 'string' ? [o, o] : o;
          return (
            <option key={v} value={v}>
              {text}
            </option>
          );
        })}
      </select>
    </label>
  );
}

/** 見出しつきの入力欄。 */
export function TextField({
  label,
  value,
  onChange,
  numeric = false,
  unit,
  placeholder,
}: {
  readonly label: string;
  readonly value: string;
  readonly onChange: (v: string) => void;
  readonly numeric?: boolean;
  /** 見出しに添える単位（mm/s など）。 */
  readonly unit?: string | undefined;
  readonly placeholder?: string | undefined;
}) {
  return (
    <label className="flex flex-col gap-1.5 text-sm font-bold">
      {unit === undefined ? label : `${label}（${unit}）`}
      <input
        aria-label={label}
        type={numeric ? 'number' : 'text'}
        inputMode={numeric ? 'numeric' : undefined}
        step={numeric ? 1 : undefined}
        placeholder={placeholder}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className={`${field} ${numeric ? 'font-number text-[17px] font-semibold' : ''}`}
      />
    </label>
  );
}

/** 表の行の操作ボタンと、押せない理由。 */
export function RowActions({ reason, children }: { readonly reason: string | null; readonly children: ReactNode }) {
  return (
    <>
      <div className="flex justify-end gap-2">{children}</div>
      {reason !== null && <p className={`${note} mt-1 ml-auto max-w-64 text-right`}>{reason}</p>}
    </>
  );
}
