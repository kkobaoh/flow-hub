import { useState } from 'react';
import type { RunIndicator } from '../server/systemStatus';
import { ConfirmDialog } from './ConfirmDialog';

export interface RunSwitchProps {
  /** 運転状態。わからないときは、どちらも押されていない形にして、押せなくする。 */
  readonly run: RunIndicator;
  /** 押せなくするか（サーバーとつながっていない、送っている途中など）。 */
  readonly disabled: boolean;
  /** 確かめたうえで、スタートにする。 */
  readonly onStart: () => void;
  /** 確かめたうえで、停止にする。 */
  readonly onStop: () => void;
}

type Asking = 'start' | 'stop' | null;

const confirmTexts = {
  stop: {
    title: '運転を停止しますか',
    body: '新しい搬送を割り当てなくなり、走っている搬送は一時停止します。AGV は、近くの待機パネルなどへ移って止まります。マニュアル操作は、停止のあいだも使えます。',
    label: '停止する',
    tone: 'neutral',
  },
  start: {
    title: '運転をスタートしますか',
    body: '搬送の割り当てを始めます。一時停止していた搬送は、続きから走ります。',
    label: 'スタートする',
    tone: 'primary',
  },
} as const;

const side = 'min-h-11 rounded-md px-4 disabled:cursor-not-allowed disabled:opacity-60 text-[15px] font-bold focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-focus';

/** 運転のスタート・停止の切り替え。システム全体に効くので、確かめる画面を出してから切り替える。 */
export function RunSwitch({ run, disabled, onStart, onStop }: RunSwitchProps) {
  const [asking, setAsking] = useState<Asking>(null);
  const locked = disabled || run === 'unknown';
  // 確かめている途中で押せなくなったら（つながりが切れたなど）、確かめる画面も閉じる
  if (locked && asking !== null) {
    setAsking(null);
  }
  const texts = asking ? confirmTexts[asking] : null;
  const running = run === 'running';

  return (
    <>
      <div role="group" aria-label="運転状態" className="flex items-center gap-1.5 rounded-[10px] bg-graphite p-2">
        <span className="px-1 text-[13px] text-header-muted">運転</span>
        <button
          type="button"
          aria-pressed={running}
          disabled={locked}
          onClick={() => run === 'stopped' && setAsking('start')}
          className={`${side} ${running ? 'bg-running text-white' : 'bg-transparent text-header-muted'}`}
        >
          スタート
        </button>
        <button
          type="button"
          aria-pressed={run === 'stopped'}
          disabled={locked}
          onClick={() => running && setAsking('stop')}
          className={`${side} ${run === 'stopped' ? 'bg-paper text-graphite' : 'bg-transparent text-header-muted'}`}
        >
          停止
        </button>
      </div>
      {asking && texts && (
        <ConfirmDialog
          title={texts.title}
          body={texts.body}
          confirmLabel={texts.label}
          tone={texts.tone}
          onCancel={() => setAsking(null)}
          onConfirm={() => {
            setAsking(null);
            if (asking === 'start') {
              onStart();
            } else {
              onStop();
            }
          }}
        />
      )}
    </>
  );
}
