import { parseVersion, type Versioned } from '../../../shared/realtime/versioned';

/** サーバーに残しているメッセージ。番号は、知らせた順に 1 から付く。 */
export interface ServerMessage {
  readonly sequence: number;
  readonly at: Date;
  readonly title: string;
  readonly detail: string;
}

/** サーバーから届くメッセージの様子（残しているもの全部、新しい順）。増えるたびに番号が進む。 */
export interface MessagesSnapshot extends Versioned {
  readonly messages: readonly ServerMessage[];
}

function parseMessage(value: unknown): ServerMessage {
  if (typeof value !== 'object' || value === null) {
    throw new TypeError('メッセージの形が正しくありません。');
  }
  const { sequence, at, title, detail } = value as Record<string, unknown>;
  if (typeof sequence !== 'number' || !Number.isInteger(sequence) || sequence < 1) {
    throw new TypeError(`メッセージの番号が正しくありません（${String(sequence)}）。`);
  }
  const time = typeof at === 'string' ? new Date(at) : null;
  if (time === null || Number.isNaN(time.getTime())) {
    throw new TypeError(`メッセージの時刻が読めません（${String(at)}）。`);
  }
  if (typeof title !== 'string' || title.trim() === '') {
    throw new TypeError('メッセージの見出しがありません。');
  }
  if (typeof detail !== 'string') {
    throw new TypeError('メッセージの中身が正しくありません。');
  }
  return { sequence, at: time, title, detail };
}

/** サーバーから届いた値を検証して、メッセージの様子にする。形が正しくなければ例外にする。 */
export function parseMessages(value: unknown): MessagesSnapshot {
  if (typeof value !== 'object' || value === null) {
    throw new TypeError('メッセージの様子の形が正しくありません。');
  }
  const { version, messages } = value as Record<string, unknown>;
  if (!Array.isArray(messages)) {
    throw new TypeError('メッセージの並びがありません。');
  }
  return { version: parseVersion(version, 'メッセージの様子'), messages: messages.map(parseMessage) };
}
