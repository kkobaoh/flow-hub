/** サーバーから届く、番号つきの様子。変わるたびに番号が進む。 */
export interface Versioned {
  /** 様子の番号（1 から始まる整数）。 */
  readonly version: number;
}

/**
 * 画面が持っている、ある種類の様子。
 * - waiting：まだ一度も届いていない。
 * - live：つながっていて、届いた様子を使っている。
 * - stale：つながりが切れて、最後に受け取った様子を出している（古いかもしれない）。
 */
export type SnapshotView<T extends Versioned> =
  | { readonly kind: 'waiting' }
  | { readonly kind: 'live'; readonly snapshot: T }
  | { readonly kind: 'stale'; readonly snapshot: T };

/** 様子を変えるきっかけ。 */
export type SnapshotInput<T extends Versioned> = { readonly type: 'snapshot'; readonly snapshot: T } | { readonly type: 'disconnected' };

/**
 * 届いた様子や、つながりの変化で、画面が持つ様子を進める（どの種類の様子でも同じ）。
 * - 様子は丸ごと差し替える。古い番号や同じ番号の様子は捨てる（届く順が入れ替わっても、重なって届いても崩れない）。
 * - つながりが切れたら、最後の様子を古いものにする。つなぎ直したあとは、番号にかかわらず届いた様子を使う（サーバーが起動し直すと、番号は 1 から始まるため）。
 */
export function receiveSnapshot<T extends Versioned>(view: SnapshotView<T>, input: SnapshotInput<T>): SnapshotView<T> {
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

/** 様子の番号を検証する。1 以上の整数でなければ例外にする。 */
export function parseVersion(value: unknown, what: string): number {
  if (typeof value !== 'number' || !Number.isInteger(value) || value < 1) {
    throw new TypeError(`${what}の番号が正しくありません（${String(value)}）。`);
  }
  return value;
}
