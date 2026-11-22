import { describe, expect, it } from 'vitest';
import { controlOptions, type ControlInput } from './controlStatus';
import { initialRoles } from './permissions';

const base: ControlInput = {
  current: 'auto',
  operation: 'idle',
  hasAssignedTransport: false,
  permissions: initialRoles.保守,
};

function option(input: ControlInput, status: string) {
  const found = controlOptions(input).find((o) => o.status === status);
  if (!found) {
    throw new Error(`${status} がありません`);
  }
  return found;
}

describe('controlOptions（コントロールステータスの切り替え）', () => {
  it('5つのステータスを、決まった順に返す。今のステータスには印を付ける', () => {
    const options = controlOptions(base);

    expect(options.map((o) => o.status)).toEqual(['auto', 'assignmentPaused', 'manual', 'locked', 'detached']);
    expect(options.map((o) => o.current)).toEqual([true, false, false, false, false]);
  });

  it('オートと割り当て停止は、いつでも選べる', () => {
    const input: ControlInput = { ...base, current: 'manual', operation: 'traveling' };

    expect(option(input, 'auto').allowed).toBe(true);
    expect(option(input, 'assignmentPaused').allowed).toBe(true);
  });

  it('マニュアルとロックは、走っていないとき（待機中か緊急停止中）だけ選べる', () => {
    expect(option({ ...base, operation: 'idle' }, 'manual').allowed).toBe(true);
    expect(option({ ...base, operation: 'emergencyStopped' }, 'locked').allowed).toBe(true);

    const traveling = option({ ...base, operation: 'traveling' }, 'manual');
    expect(traveling.allowed).toBe(false);
    expect(traveling.reason).toBe('走っているあいだは切り替えられません。');
    expect(option({ ...base, operation: 'fault' }, 'locked').allowed).toBe(false);
  });

  it('切り離しは、ロック中だけ選べる', () => {
    expect(option({ ...base, current: 'locked' }, 'detached').allowed).toBe(true);

    const fromAuto = option(base, 'detached');
    expect(fromAuto.allowed).toBe(false);
    expect(fromAuto.reason).toBe('ロック中のときだけ切り替えられます。先にロックにしてください。');
  });

  it('割り当て済みの搬送があるあいだは、オート以外（マニュアル・ロック・切り離し）を選べない', () => {
    const input: ControlInput = { ...base, current: 'locked', hasAssignedTransport: true };

    expect(option(input, 'manual').allowed).toBe(false);
    expect(option(input, 'manual').reason).toBe('割り当て済みの搬送があります。先に割り当て停止で搬送を終えるか、搬送を取りやめてください。');
    expect(option(input, 'detached').allowed).toBe(false);
    expect(option(input, 'assignmentPaused').allowed).toBe(true);
  });

  it('切り離しから戻すときは、位置の指定が要ることを知らせる', () => {
    const input: ControlInput = { ...base, current: 'detached' };

    expect(option(input, 'auto')).toMatchObject({ allowed: true, needsPosition: true });
    expect(option(input, 'locked')).toMatchObject({ allowed: true, needsPosition: true });
    expect(option({ ...base, current: 'auto' }, 'assignmentPaused').needsPosition).toBe(false);
  });

  it('運用のロールは、オートと割り当て停止の間だけ切り替えられる', () => {
    const input: ControlInput = { ...base, permissions: initialRoles.運用 };

    expect(option(input, 'assignmentPaused').allowed).toBe(true);
    expect(option(input, 'manual').allowed).toBe(false);
    expect(option(input, 'manual').reason).toBe('保守以上のロールで切り替えられます。');
    // マニュアル中の AGV を、運用のロールはオートに戻せない（マニュアルからの切り替えも保守の操作）
    expect(option({ ...input, current: 'manual' }, 'auto').allowed).toBe(false);
  });

  it('閲覧のみのロールは、何も切り替えられない', () => {
    const options = controlOptions({ ...base, permissions: initialRoles.閲覧のみ });

    expect(options.filter((o) => !o.current).every((o) => !o.allowed)).toBe(true);
  });

  it('今のステータスは、選べるものとして扱わない（押しても何もしない）', () => {
    expect(option(base, 'auto')).toMatchObject({ current: true, allowed: false });
  });
});
