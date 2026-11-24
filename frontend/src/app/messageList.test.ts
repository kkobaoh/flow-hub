import { describe, expect, it } from 'vitest';
import type { ServerMessage } from '../server/messages';
import { formatMessageTime, latestSequence, railMessages, readSeenUpTo, writeSeenUpTo, type LocalMessage } from './messageList';

const at = (iso: string) => new Date(iso);
const server = (sequence: number, iso: string): ServerMessage => ({ sequence, at: at(iso), title: `知らせ${sequence}`, detail: `中身${sequence}` });
const now = at('2026-11-24T15:00:00');

describe('railMessages（メッセージの列に出すもの）', () => {
  it('サーバーのメッセージは、見たところより新しい番号だけ「まだ見ていない」にする', () => {
    const list = railMessages([server(3, '2026-11-24T10:00:00'), server(2, '2026-11-24T09:00:00')], 2, [], now);

    expect(list.map((m) => [m.title, m.unread])).toEqual([
      ['知らせ3', true],
      ['知らせ2', false],
    ]);
  });

  it('初めて開いたブラウザー（見たところが 0）では、すべて「まだ見ていない」', () => {
    expect(railMessages([server(2, '2026-11-24T09:00:00'), server(1, '2026-11-24T08:00:00')], 0, [], now).every((m) => m.unread)).toBe(true);
  });

  it('その画面だけの知らせを、時刻の順に混ぜて並べる', () => {
    const local: LocalMessage = { id: 'run-1', at: at('2026-11-24T09:30:00'), title: '運転をスタートできませんでした', detail: '', unread: true };

    const list = railMessages([server(3, '2026-11-24T10:00:00'), server(2, '2026-11-24T09:00:00')], 3, [local], now);

    expect(list.map((m) => m.title)).toEqual(['知らせ3', '運転をスタートできませんでした', '知らせ2']);
    expect(list[1]?.unread).toBe(true);
  });

  it('時刻は、今日なら時と分、ほかの日なら月日も出す', () => {
    expect(formatMessageTime(at('2026-11-24T09:05:00'), now)).toBe('09:05');
    expect(formatMessageTime(at('2026-11-23T17:48:00'), now)).toBe('11/23 17:48');
  });
});

describe('latestSequence', () => {
  it('いちばん新しい番号（なければ 0）', () => {
    expect(latestSequence([server(2, '2026-11-24T09:00:00'), server(5, '2026-11-24T10:00:00')])).toBe(5);
    expect(latestSequence([])).toBe(0);
  });
});

describe('どこまで見たかを、ブラウザーに覚える', () => {
  const memory = (): Storage => {
    const data = new Map<string, string>();
    return {
      get length() {
        return data.size;
      },
      clear: () => data.clear(),
      getItem: (k) => data.get(k) ?? null,
      key: (i) => [...data.keys()][i] ?? null,
      removeItem: (k) => void data.delete(k),
      setItem: (k, v) => void data.set(k, v),
    };
  };

  it('書いた番号を読み直せる。覚えていなければ 0', () => {
    const storage = memory();
    expect(readSeenUpTo(storage)).toBe(0);

    writeSeenUpTo(storage, 7);

    expect(readSeenUpTo(storage)).toBe(7);
  });

  it('読めない値や、使えないブラウザーの置き場では 0（画面は止めない）', () => {
    const storage = memory();
    storage.setItem('flowhub.messages.seenUpTo', 'abc');
    expect(readSeenUpTo(storage)).toBe(0);

    const broken = { ...memory(), getItem: () => { throw new Error('使えない'); }, setItem: () => { throw new Error('使えない'); } } as Storage;
    expect(readSeenUpTo(broken)).toBe(0);
    expect(() => writeSeenUpTo(broken, 3)).not.toThrow();
  });
});
