import { useEffect, useState, type ReactNode } from 'react';
import type { RunIndicator } from '../../features/system/systemStatus';
import { AppHeader, type SignedInUser } from './AppHeader';
import { MessageRail, MessageTab, type Message } from '../../features/alarm/MessageRail';
import type { Permissions } from '../../features/auth/permissions';
import type { ScreenKey } from '../screens';
import { clickedInside } from '../../shared/utils/outside';

export interface MainLayoutProps {
  readonly current: ScreenKey;
  /** 画面の見出し。 */
  readonly title: string;
  readonly run: RunIndicator;
  readonly user: SignedInUser;
  readonly permissions: Permissions;
  readonly mapVersion: string;
  readonly messages: readonly Message[];
  readonly onMarkAllSeen: () => void;
  readonly onNavigate: (screen: ScreenKey) => void;
  readonly onChangePassword: () => void;
  readonly onLogout: () => void;
  /** 見出しの右に置くもの（「〜を加える」のボタンなど）。 */
  readonly actions?: ReactNode;
  readonly children: ReactNode;
}

/**
 * マップ以外の画面の枠（上の帯、画面の見出し、メッセージの列）。
 * メッセージの列は、最初はたたんでおき、開いたときは中身を押し縮めずに右から重ねて出す（docs/screens.md）。
 */
export function MainLayout(props: MainLayoutProps) {
  const [open, setOpen] = useState(false);
  const unread = props.messages.filter((m) => m.unread).length;

  useEffect(() => {
    if (!open) {
      return undefined;
    }
    const closeOutside = (e: MouseEvent) => {
      if (!clickedInside(e, '[data-message-area]')) {
        setOpen(false);
      }
    };
    document.addEventListener('click', closeOutside);
    return () => document.removeEventListener('click', closeOutside);
  }, [open]);

  return (
    <div className="flex min-h-screen flex-col bg-ground font-sans text-graphite">
      <AppHeader
        run={props.run}
        user={props.user}
        current={props.current}
        mapVersion={props.mapVersion}
        onNavigate={props.onNavigate}
        onChangePassword={props.onChangePassword}
        onLogout={props.onLogout}
      />
      <main className="mx-auto box-border flex w-full max-w-[1200px] flex-col gap-4 px-4 pt-6 pb-12 sm:px-5">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h1 className="m-0 text-2xl font-bold">{props.title}</h1>
          {props.actions && <div className="flex flex-wrap items-center gap-2">{props.actions}</div>}
        </div>
        {props.children}
      </main>
      <div data-message-area="">
        <div className={`fixed top-[76px] z-14 ${open ? 'right-[340px] max-sm:right-0' : 'right-0'}`}>
          <MessageTab open={open} unreadCount={unread} onToggle={() => setOpen((v) => !v)} />
        </div>
        {open && (
          <aside
            id="message-rail"
            className="fixed inset-y-0 right-0 z-30 w-[340px] overflow-y-auto bg-paper border-l border-line shadow-[-6px_0_20px_rgba(35,38,43,0.18)] max-sm:top-auto max-sm:left-0 max-sm:h-[55vh] max-sm:w-auto max-sm:animate-sheet-in max-sm:rounded-t-2xl max-sm:border-t max-sm:border-l-0"
          >
            <MessageRail messages={props.messages} onMarkAllSeen={props.onMarkAllSeen} onClose={() => setOpen(false)} />
          </aside>
        )}
      </div>
    </div>
  );
}
