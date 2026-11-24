import { describe, expect, it } from 'vitest';
import { parseMessages } from './messages';

describe('parseMessages', () => {
  it('サーバーから届いたメッセージの様子を、型にする（時刻は Date にする）', () => {
    const value = {
      version: 3,
      messages: [
        { sequence: 2, at: '2026-11-24T01:05:00+00:00', title: '行ける充電ポートがありません', detail: 'AGV-2 は、充電ポートが空いたら向かいます。' },
        { sequence: 1, at: '2026-11-24T01:00:00Z', title: '搬送を中断しました', detail: '' },
      ],
    };

    const parsed = parseMessages(value);

    expect(parsed.version).toBe(3);
    expect(parsed.messages.map((m) => m.sequence)).toEqual([2, 1]);
    expect(parsed.messages[0]?.at.toISOString()).toBe('2026-11-24T01:05:00.000Z');
    expect(parsed.messages[1]?.title).toBe('搬送を中断しました');
  });

  it.each([
    ['並びがない', { version: 1 }],
    ['番号が正しくない', { version: 0, messages: [] }],
    ['メッセージの番号が正しくない', { version: 1, messages: [{ sequence: 0, at: '2026-11-24T01:00:00Z', title: 'a', detail: '' }] }],
    ['時刻が読めない', { version: 1, messages: [{ sequence: 1, at: 'きのう', title: 'a', detail: '' }] }],
    ['見出しが空', { version: 1, messages: [{ sequence: 1, at: '2026-11-24T01:00:00Z', title: '', detail: '' }] }],
    ['中身が文字でない', { version: 1, messages: [{ sequence: 1, at: '2026-11-24T01:00:00Z', title: 'a', detail: null }] }],
  ])('形が正しくなければ、例外にする（%s）', (_, value) => {
    expect(() => parseMessages(value)).toThrow();
  });
});
