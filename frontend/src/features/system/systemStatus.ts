import { parseVersion, receiveSnapshot, type SnapshotInput, type SnapshotView, type Versioned } from '../../shared/realtime/versioned';

/** 運転状態。サーバーの <c>GET /system/status</c> と、ハブの <c>systemStatus</c> と同じ言葉。 */
export type RunState = 'started' | 'stopped';

/** サーバーから届く運転状態の様子。変わるたびに番号が進む。 */
export interface SystemStatusSnapshot extends Versioned {
  readonly state: RunState;
}

/** 画面が持っている運転状態の様子。 */
export type StatusView = SnapshotView<SystemStatusSnapshot>;

/** 運転状態の様子を変えるきっかけ。 */
export type StatusInput = SnapshotInput<SystemStatusSnapshot>;

/** 画面に出す運転状態。届いていなければ、わからない。 */
export type RunIndicator = 'running' | 'stopped' | 'unknown';

/** サーバーから届いた値を検証して、運転状態の様子にする。形が正しくなければ例外にする。 */
export function parseSystemStatus(value: unknown): SystemStatusSnapshot {
  if (typeof value !== 'object' || value === null) {
    throw new TypeError('運転状態の様子の形が正しくありません。');
  }
  const { version, state } = value as Record<string, unknown>;
  if (state !== 'started' && state !== 'stopped') {
    throw new TypeError(`知らない運転状態です（${String(state)}）。`);
  }
  return { version: parseVersion(version, '運転状態の様子'), state };
}

/** 届いた運転状態の様子や、つながりの変化で、画面が持つ様子を進める（versioned.ts の receiveSnapshot）。 */
export const receiveStatus: (view: StatusView, input: StatusInput) => StatusView = receiveSnapshot;

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
