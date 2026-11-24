import { useEffect, useRef, useState } from 'react';
import type { RunIndicator } from '../../features/system/systemStatus';
import { screens, type ScreenKey } from '../screens';

export interface SignedInUser {
  readonly name: string;
  /** ロールの名前。 */
  readonly role: string;
}

export interface AppHeaderProps {
  /** 運転状態。サーバーから届くまでは、わからない。 */
  readonly run: RunIndicator;
  readonly user: SignedInUser;
  /** 今いる画面。 */
  readonly current: ScreenKey;
  /** メニューの一番下に出す、マップの版。 */
  readonly mapVersion: string;
  readonly onNavigate: (screen: ScreenKey) => void;
  readonly onChangePassword: () => void;
  readonly onLogout: () => void;
}

/** 帯に出す運転状態の言葉。 */
const runLabels: Record<RunIndicator, string> = { running: '運転中', stopped: '停止中', unknown: '運転状態を確かめています' };

const menuItem =
  'flex min-h-11 w-full items-center rounded-md px-3 text-left text-[15px] text-graphite hover:bg-ground focus-visible:outline-3 focus-visible:outline-focus';
const smallButton =
  'min-h-11 rounded-lg border border-control-border bg-white px-2.5 text-[13px] font-bold text-graphite focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-focus';

/** どの画面にも出す上の帯。左に名前と運転状態、右にユーザーとメニュー。名前を押すと、マップの画面へ移る。 */
export function AppHeader({ run, user, current, mapVersion, onNavigate, onChangePassword, onLogout }: AppHeaderProps) {
  const [open, setOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) {
      return undefined;
    }
    const closeOutside = (e: MouseEvent) => {
      if (menuRef.current && !e.composedPath().includes(menuRef.current)) {
        setOpen(false);
      }
    };
    document.addEventListener('click', closeOutside);
    return () => document.removeEventListener('click', closeOutside);
  }, [open]);

  const choose = (screen: ScreenKey) => {
    setOpen(false);
    onNavigate(screen);
  };
  const act = (fn: () => void) => () => {
    setOpen(false);
    fn();
  };
  const entry = (key: ScreenKey, title: string) => (
    <button
      key={key}
      type="button"
      data-screen={key}
      aria-current={key === current ? 'page' : undefined}
      onClick={() => choose(key)}
      className={`${menuItem}${key === current ? ' bg-ground font-bold' : ''}`}
    >
      {title}
    </button>
  );

  return (
    <header className="relative z-15 flex flex-wrap items-center gap-x-5 gap-y-3 bg-graphite px-3 py-2 text-white sm:px-5 sm:py-2.5">
      <button
        type="button"
        data-home=""
        aria-label="FlowHub（マップの画面へ）"
        onClick={() => choose('map')}
        className="-mx-1 rounded-md px-1 font-number text-2xl leading-none font-bold text-white focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-focus"
      >
        FlowHub
      </button>
      <div
        role="status"
        className={`flex items-center gap-2 rounded-full px-3 py-1.5 text-sm font-bold ${run === 'running' ? 'bg-running text-white' : 'bg-paper text-graphite'}`}
      >
        {run !== 'unknown' && <span aria-hidden="true" className={`size-2.5 rounded-full ${run === 'running' ? 'bg-running-dot' : 'bg-stopped-dot'}`} />}
        {runLabels[run]}
      </div>
      <div className="flex-1" />
      <span className="hidden text-sm text-header-muted sm:inline">
        {user.name}（{user.role}）
      </span>
      <div ref={menuRef} className="relative">
        <button
          type="button"
          aria-label="メニュー"
          aria-expanded={open}
          aria-controls="main-menu"
          onClick={() => setOpen((v) => !v)}
          className="flex min-h-11 items-center gap-2 rounded-lg border border-header-border px-3 text-sm font-bold text-white focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-focus"
        >
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
            <path d="M4 7h16" />
            <path d="M4 12h16" />
            <path d="M4 17h16" />
          </svg>
          <span className="hidden sm:inline">メニュー</span>
        </button>
        {open && (
          <nav
            id="main-menu"
            aria-label="画面"
            onKeyDown={(e) => {
              if (e.key === 'Escape') {
                setOpen(false);
              }
            }}
            className="fixed inset-x-3 top-16 z-40 flex flex-col rounded-[10px] bg-paper p-2 text-graphite shadow-[0_8px_24px_rgba(35,38,43,0.25)] sm:absolute sm:inset-x-auto sm:top-[calc(100%+8px)] sm:right-0 sm:w-[300px]"
          >
            <div className="mb-1 flex flex-wrap items-center justify-between gap-2 border-b border-line px-3 pt-2 pb-3">
              <div className="flex flex-col leading-snug">
                <span className="text-[15px] font-bold">{user.name}</span>
                <span className="text-[13px] text-ink-muted">{user.role}</span>
              </div>
              <div className="flex gap-1.5">
                <button type="button" onClick={act(onChangePassword)} className={smallButton}>
                  パスワードを変える
                </button>
                <button type="button" onClick={act(onLogout)} className={smallButton}>
                  ログアウト
                </button>
              </div>
            </div>
            {screens.filter((s) => !s.setting).map((s) => entry(s.key, s.title))}
            <div className="mx-3 mt-2 mb-1 border-t border-line pt-2 text-[13px] font-bold text-ink-muted">設定</div>
            {screens.filter((s) => s.setting).map((s) => entry(s.key, s.title))}
            <div className="mx-3 mt-2 mb-1 border-t border-line pt-2 text-[13px] text-ink-muted">{mapVersion}</div>
          </nav>
        )}
      </div>
    </header>
  );
}
