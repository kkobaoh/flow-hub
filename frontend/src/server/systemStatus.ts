/** 運転状態。サーバーの <c>GET /system/status</c> と、ハブの <c>systemStatus</c> と同じ言葉。 */
export type RunState = 'started' | 'stopped';

/** サーバーから届く運転状態の様子。変わるたびに番号が進む。 */
export interface SystemStatusSnapshot {
  /** 様子の番号（1 から始まる整数）。 */
  readonly version: number;
  readonly state: RunState;
}

/**
 * 画面が持っている運転状態の様子。
 * - waiting：まだ一度も届いていない。
 * - live：つながっていて、届いた様子を使っている。
 * - stale：つながりが切れて、最後に受け取った様子を出している（古いかもしれない）。
 */
export type StatusView =
  | { readonly kind: 'waiting' }
  | { readonly kind: 'live'; readonly snapshot: SystemStatusSnapshot }
  | { readonly kind: 'stale'; readonly snapshot: SystemStatusSnapshot };

/** 様子を変えるきっかけ。 */
export type StatusInput = { readonly type: 'snapshot'; readonly snapshot: SystemStatusSnapshot } | { readonly type: 'disconnected' };

/** 画面に出す運転状態。届いていなければ、わからない。 */
export type RunIndicator = 'running' | 'stopped' | 'unknown';

/** サーバーから届いた値を検証して、運転状態の様子にする。形が正しくなければ例外にする。 */
export function parseSystemStatus(value: unknown): SystemStatusSnapshot {
  if (typeof value !== 'object' || value === null) {
    throw new TypeError('運転状態の様子の形が正しくありません。');
  }
  const { version, state } = value as Record<string, unknown>;
  if (typeof version !== 'number' || !Number.isInteger(version) || version < 1) {
    throw new TypeError(`運転状態の様子の番号が正しくありません（${String(version)}）。`);
  }
  if (state !== 'started' && state !== 'stopped') {
    throw new TypeError(`知らない運転状態です（${String(state)}）。`);
  }
  return { version, state };
}

/**
 * 届いた様子や、つながりの変化で、画面が持つ様子を進める。
 * - 様子は丸ごと差し替える。古い番号や同じ番号の様子は捨てる（届く順が入れ替わっても、重なって届いても崩れない）。
 * - つながりが切れたら、最後の様子を古いものにする。つなぎ直したあとは、番号にかかわらず届いた様子を使う（サーバーが起動し直すと、番号は 1 から始まるため）。
 */
export function receiveStatus(view: StatusView, input: StatusInput): StatusView {
  switch (input.type) {
    case 'snapshot':
      if (view.kind === 'live' && input.snapshot.version <= view.snapshot.version) {
        return view;
      }
      return { kind: 'live', snapshot: input.snapshot };
    case 'disconnected':
      return view.kind === 'live' ? { kind: 'stale', snapshot: view.snapshot } : view;
    default:
      throw new Error(`知らないきっかけです: ${JSON.stringify(input satisfies never)}`);
  }
}

/** 画面に出す運転状態。切れている間は、最後に受け取ったものを出す（古いことは、つながりの帯で知らせる）。 */
export function runIndicator(view: StatusView): RunIndicator {
  switch (view.kind) {
    case 'waiting':
      return 'unknown';
    case 'live':
    case 'stale':
      return view.snapshot.state === 'started' ? 'running' : 'stopped';
    default:
      throw new Error(`知らない様子です: ${JSON.stringify(view satisfies never)}`);
  }
}
