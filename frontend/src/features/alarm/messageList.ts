import type { ServerMessage } from './api/messages';
import type { Message } from './MessageRail';

/** その画面だけの知らせ（操作がサーバーに届かなかった、など）。サーバーには残せないので、読み込み直すと消える。 */
export interface LocalMessage {
  readonly id: string;
  readonly at: Date;
  readonly title: string;
  readonly detail: string;
  readonly unread: boolean;
}

const pad = (n: number) => String(n).padStart(2, '0');

/** メッセージの時刻：今日なら「09:05」、ほかの日なら「11/23 17:48」。 */
export function formatMessageTime(at: Date, now: Date): string {
  const clock = `${pad(at.getHours())}:${pad(at.getMinutes())}`;
  const sameDay = at.getFullYear() === now.getFullYear() && at.getMonth() === now.getMonth() && at.getDate() === now.getDate();
  return sameDay ? clock : `${at.getMonth() + 1}/${at.getDate()} ${clock}`;
}

/** いちばん新しいメッセージの番号（なければ 0）。「すべて見たことにする」で、ここまで見たことにする。 */
export function latestSequence(messages: readonly ServerMessage[]): number {
  return messages.reduce((max, m) => Math.max(max, m.sequence), 0);
}

/**
 * メッセージの列に出すもの（新しい順）。サーバーのメッセージは、見たところ（seenUpTo）より新しい番号だけ「まだ見ていない」にする。
 * その画面だけの知らせは、時刻の順に混ぜて並べる（docs/screens.md「メッセージの列」）。
 */
export function railMessages(server: readonly ServerMessage[], seenUpTo: number, local: readonly LocalMessage[], now: Date): Message[] {
  const rows = [
    ...server.map((m) => ({ at: m.at, order: m.sequence, message: { id: `server-${m.sequence}`, time: formatMessageTime(m.at, now), title: m.title, detail: m.detail, unread: m.sequence > seenUpTo } })),
    ...local.map((m) => ({ at: m.at, order: 0, message: { id: `local-${m.id}`, time: formatMessageTime(m.at, now), title: m.title, detail: m.detail, unread: m.unread } })),
  ];
  return rows.sort((a, b) => b.at.getTime() - a.at.getTime() || b.order - a.order).map((r) => r.message);
}

const SEEN_KEY = 'flowhub.messages.seenUpTo';

/** どこまで見たか（メッセージの番号）を、ブラウザーから読む。覚えていない、読めないときは 0（すべて「まだ見ていない」）。 */
export function readSeenUpTo(storage: Storage | null): number {
  try {
    const value = Number(storage?.getItem(SEEN_KEY) ?? '0');
    return Number.isInteger(value) && value > 0 ? value : 0;
  } catch {
    return 0;
  }
}

/** どこまで見たかを、ブラウザーに覚える。覚えられなくても、画面は止めない（その画面の中では覚えている）。 */
export function writeSeenUpTo(storage: Storage | null, sequence: number): void {
  try {
    storage?.setItem(SEEN_KEY, String(sequence));
  } catch {
    // プライベートなウィンドウなどで使えないときは、あきらめる
  }
}
