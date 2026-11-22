import { describe, expect, it } from 'vitest';
import { heading } from '../map/model';
import { manualAvailability, outcomeMessage, type ManualInput } from './manual';

const idle: ManualInput = { operation: 'idle', activity: 'none', heading: heading(90) };

describe('manualAvailability（マニュアルで送れるもの）', () => {
  it('待機中で、何も走らせていなければ、すべて送れる', () => {
    const a = manualAvailability(idle);

    expect(a.destination.allowed).toBe(true);
    expect(a.step.allowed).toBe(true);
    expect(a.forwardBackward.allowed).toBe(true);
    expect(a.carrier.allowed).toBe(true);
  });

  it('行き先へ走っている途中は、ほかの指示を送れない', () => {
    const a = manualAvailability({ ...idle, operation: 'traveling', activity: 'destination' });

    expect(a.destination).toEqual({ allowed: false, reason: '行き先へ走っている途中は、ほかの指示を送れません。' });
    expect(a.step.allowed).toBe(false);
    expect(a.carrier.allowed).toBe(false);
  });

  it('1つずつの動作を走らせている途中は、1つずつの動作だけを、あとに並べられる', () => {
    const a = manualAvailability({ ...idle, operation: 'traveling', activity: 'steps' });

    expect(a.step.allowed).toBe(true);
    expect(a.destination.allowed).toBe(false);
    expect(a.destination.reason).toBe('1つずつの動作を走らせている途中は、行き先を送れません。');
    expect(a.carrier.allowed).toBe(false);
  });

  it('緊急停止中、異常、位置リセット待ちのときは、何も送れない', () => {
    for (const [operation, reason] of [
      ['emergencyStopped', '緊急停止中は送れません。アラームクリアのあとに送ってください。'],
      ['fault', '異常のあいだは送れません。'],
      ['awaitingPositionReset', '位置リセットのあとに送ってください。'],
    ] as const) {
      const a = manualAvailability({ ...idle, operation, activity: 'steps' });
      expect(a.destination).toEqual({ allowed: false, reason });
      expect(a.step).toEqual({ allowed: false, reason });
      expect(a.carrier).toEqual({ allowed: false, reason });
    }
  });

  it('前進と後退は、0・90・180・270度のどれかを向いているときだけ送れる', () => {
    expect(manualAvailability({ ...idle, heading: heading(270) }).forwardBackward.allowed).toBe(true);

    const slanted = manualAvailability({ ...idle, heading: heading(45) }).forwardBackward;
    expect(slanted).toEqual({ allowed: false, reason: '前進と後退は、0・90・180・270度のどれかを向いているときだけ送れます。先に「回転」でラインの向きに合わせてください。' });
  });
});

describe('outcomeMessage（送った結果の知らせ）', () => {
  it('受け付けなかった理由を、ユーザーにわかる言葉にする', () => {
    expect(outcomeMessage('move', 'accepted')).toBeNull();
    expect(outcomeMessage('move', 'alreadyThere')).toBe('すでにそのパネルにいます。');
    expect(outcomeMessage('move', 'noRoute')).toBe('マップの上に、そのパネルまでの経路がありません。');
    expect(outcomeMessage('move', 'blocked')).toBe('ほかの AGV に塞がれていて、今は行けません。');
    expect(outcomeMessage('step', 'blocked')).toBe('動く先のパネルを予約できないので、送りませんでした。');
    expect(outcomeMessage('step', 'notAllowed')).toBe('マップの決まりを守れない動作なので、送りませんでした。');
  });
});
