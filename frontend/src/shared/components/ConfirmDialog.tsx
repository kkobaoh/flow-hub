import { useEffect, useId, useRef } from 'react';

export interface ConfirmDialogProps {
  readonly title: string;
  readonly body: string;
  /** 実行するボタンの文言（「停止する」など。何が起きるかを書く）。 */
  readonly confirmLabel: string;
  /** 実行するボタンの色。danger は止める・消すなど、影響の大きい操作。 */
  readonly tone: 'primary' | 'danger' | 'neutral';
  readonly onConfirm: () => void;
  readonly onCancel: () => void;
}

const toneClass: Record<ConfirmDialogProps['tone'], string> = {
  primary: 'bg-running text-white',
  danger: 'bg-estop text-white',
  neutral: 'bg-graphite text-white',
};

const buttonBase =
  'min-h-11 rounded-lg px-4.5 text-[15px] font-bold focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-focus';

/** 影響の大きい操作の前に出す、確かめる画面。開いたら「やめる」に焦点を置き、Esc でも閉じる。 */
export function ConfirmDialog({ title, body, confirmLabel, tone, onConfirm, onCancel }: ConfirmDialogProps) {
  const titleId = useId();
  const bodyId = useId();
  const cancelRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    cancelRef.current?.focus();
  }, []);

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-graphite/55 p-4">
      <div
        role="alertdialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={bodyId}
        onKeyDown={(e) => {
          if (e.key === 'Escape') {
            onCancel();
          }
        }}
        className="flex w-full max-w-[440px] flex-col gap-3 rounded-xl bg-paper p-6 text-graphite"
      >
        <h2 id={titleId} className="m-0 text-xl font-bold">
          {title}
        </h2>
        <p id={bodyId} className="m-0 text-[15px] leading-relaxed">
          {body}
        </p>
        <div className="mt-2 flex flex-wrap justify-end gap-2">
          <button ref={cancelRef} type="button" onClick={onCancel} className={`${buttonBase} border border-control-border bg-white text-graphite`}>
            やめる
          </button>
          <button type="button" onClick={onConfirm} className={`${buttonBase} ${toneClass[tone]}`}>
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
