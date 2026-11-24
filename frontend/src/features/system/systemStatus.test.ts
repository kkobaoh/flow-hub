import { describe, expect, it } from 'vitest';
import { parseSystemStatus, receiveStatus, runIndicator, type StatusView } from './systemStatus';

const waiting: StatusView = { kind: 'waiting' };
const started = (version: number) => ({ version, state: 'started' as const });
const stopped = (version: number) => ({ version, state: 'stopped' as const });

describe('parseSystemStatus', () => {
  it('サーバーから届いた運転状態の様子を、型にする', () => {
    expect(parseSystemStatus({ version: 3, state: 'stopped' })).toEqual(stopped(3));
  });

  it.each([
    ['null', null],
    ['番号がない', { state: 'started' }],
    ['番号が0', { version: 0, state: 'started' }],
    ['番号が整数でない', { version: 1.5, state: 'started' }],
    ['知らない運転状態', { version: 1, state: 'paused' }],
  ])('形が正しくなければ、例外にする（%s）', (_, value) => {
    expect(() => parseSystemStatus(value)).toThrow(TypeError);
  });
});

describe('receiveStatus', () => {
  it('まだ何も届いていなければ、届いた様子をそのまま使う', () => {
    expect(receiveStatus(waiting, { type: 'snapshot', snapshot: started(4) })).toEqual({ kind: 'live', snapshot: started(4) });
  });

  it('新しい番号の様子に、丸ごと差し替える', () => {
    const view: StatusView = { kind: 'live', snapshot: started(2) };

    expect(receiveStatus(view, { type: 'snapshot', snapshot: stopped(3) })).toEqual({ kind: 'live', snapshot: stopped(3) });
  });

  it('古い番号や同じ番号の様子は捨てる（届く順が入れ替わったり、重なって届いたりしても崩れない）', () => {
    const view: StatusView = { kind: 'live', snapshot: stopped(5) };

    expect(receiveStatus(view, { type: 'snapshot', snapshot: started(4) })).toBe(view);
    expect(receiveStatus(view, { type: 'snapshot', snapshot: started(5) })).toBe(view);
  });

  it('つながりが切れたら、最後に受け取った様子を古いものにする', () => {
    expect(receiveStatus({ kind: 'live', snapshot: started(2) }, { type: 'disconnected' })).toEqual({ kind: 'stale', snapshot: started(2) });
    expect(receiveStatus(waiting, { type: 'disconnected' })).toBe(waiting);
  });

  it('つなぎ直したあとは、番号にかかわらず届いた様子を使う（サーバーが起動し直すと、番号は1から始まる）', () => {
    const view: StatusView = { kind: 'stale', snapshot: stopped(9) };

    expect(receiveStatus(view, { type: 'snapshot', snapshot: started(1) })).toEqual({ kind: 'live', snapshot: started(1) });
  });
});

describe('runIndicator', () => {
  it('つながっていて様子が届いていれば、その運転状態を出す', () => {
    expect(runIndicator({ kind: 'live', snapshot: started(1) })).toBe('running');
    expect(runIndicator({ kind: 'live', snapshot: stopped(1) })).toBe('stopped');
  });

  it('まだ届いていなければ、わからない', () => {
    expect(runIndicator(waiting)).toBe('unknown');
  });

  it('切れている間は、最後に受け取った運転状態を出す（帯で古いことを知らせる）', () => {
    expect(runIndicator({ kind: 'stale', snapshot: started(1) })).toBe('running');
  });
});
