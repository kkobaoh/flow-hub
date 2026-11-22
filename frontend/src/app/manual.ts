import type { Heading } from '../map/model';
import type { AgvOperation } from './controlStatus';

/** マニュアルで、今走らせているもの。 */
export type ManualActivity = 'none' | 'destination' | 'steps';

export interface ManualInput {
  readonly operation: AgvOperation;
  readonly activity: ManualActivity;
  readonly heading: Heading;
}

export interface Availability {
  readonly allowed: boolean;
  readonly reason: string | null;
}

export interface ManualAvailability {
  readonly destination: Availability;
  /** 1つずつの動作（回転を含む）。 */
  readonly step: Availability;
  /** 前進と後退（1つずつの動作のうち、向きの決まりがあるもの）。 */
  readonly forwardBackward: Availability;
  readonly carrier: Availability;
}

const ok: Availability = { allowed: true, reason: null };
const no = (reason: string): Availability => ({ allowed: false, reason });

const STOPPED_REASON: Partial<Record<AgvOperation, string>> = {
  emergencyStopped: '緊急停止中は送れません。アラームクリアのあとに送ってください。',
  fault: '異常のあいだは送れません。',
  awaitingPositionReset: '位置リセットのあとに送ってください。',
};

const CARDINAL = new Set([0, 90, 180, 270]);

/**
 * マニュアルで送れるものと、送れない理由（docs/domain.md「コントロールステータス」）。
 * 画面でも先に防ぐが、最後に確かめるのはサーバー。
 */
export function manualAvailability(input: ManualInput): ManualAvailability {
  const stopped = STOPPED_REASON[input.operation];
  if (stopped) {
    const blocked = no(stopped);
    return { destination: blocked, step: blocked, forwardBackward: blocked, carrier: blocked };
  }
  const destination =
    input.activity === 'destination'
      ? no('行き先へ走っている途中は、ほかの指示を送れません。')
      : input.activity === 'steps'
        ? no('1つずつの動作を走らせている途中は、行き先を送れません。')
        : ok;
  const step = input.activity === 'destination' ? no('行き先へ走っている途中は、ほかの指示を送れません。') : ok;
  const forwardBackward = !step.allowed
    ? step
    : CARDINAL.has(input.heading)
      ? ok
      : no('前進と後退は、0・90・180・270度のどれかを向いているときだけ送れます。先に「回転」でラインの向きに合わせてください。');
  const carrier = input.activity === 'none' ? ok : no('走っている途中は、キャリアを上げ下げできません。');
  return { destination, step, forwardBackward, carrier };
}

/** サーバーが返した、送った結果。 */
export type ManualOutcome = 'accepted' | 'alreadyThere' | 'noRoute' | 'blocked' | 'notAllowed';

/** 送った結果を、ユーザーにわかる言葉にする（受け付けたときは null）。 */
export function outcomeMessage(kind: 'move' | 'step' | 'carrier', outcome: ManualOutcome): string | null {
  switch (outcome) {
    case 'accepted':
      return null;
    case 'alreadyThere':
      return 'すでにそのパネルにいます。';
    case 'noRoute':
      return 'マップの上に、そのパネルまでの経路がありません。';
    case 'blocked':
      return kind === 'move' ? 'ほかの AGV に塞がれていて、今は行けません。' : '動く先のパネルを予約できないので、送りませんでした。';
    case 'notAllowed':
      return 'マップの決まりを守れない動作なので、送りませんでした。';
  }
}
