import { afterEach, describe, expect, it, vi } from 'vitest';
import { agvId } from '../map/agv';
import { heading, panelId } from '../map/model';
import { agvCommandRequest, commandNotice, sendAgvCommand, toManualOutcome } from './agvCommands';

const agv = agvId('AGV-1');
const P3 = panelId('P3');

describe('agvCommandRequest（送る先と中身）', () => {
  it.each([
    [{ kind: 'emergencyStop' }, '/agvs/AGV-1/emergency-stop', {}],
    [{ kind: 'alarmClear' }, '/agvs/AGV-1/alarm-clear', {}],
    [{ kind: 'positionReset', position: { panel: P3, heading: heading(90) } }, '/agvs/AGV-1/position-reset', { panel: 'P3', heading: 90 }],
    [{ kind: 'control', status: 'locked', position: null }, '/agvs/AGV-1/control', { status: 'locked' }],
    [{ kind: 'control', status: 'auto', position: { panel: P3, heading: heading(0) } }, '/agvs/AGV-1/control', { status: 'auto', panel: 'P3', heading: 0 }],
    [{ kind: 'manualMove', destination: P3, arrival: null }, '/agvs/AGV-1/manual/move', { destination: 'P3' }],
    [{ kind: 'manualMove', destination: P3, arrival: heading(180) }, '/agvs/AGV-1/manual/move', { destination: 'P3', arrival: 180 }],
    [{ kind: 'manualStep', step: { kind: 'forward' } }, '/agvs/AGV-1/manual/step', { kind: 'forward' }],
    [{ kind: 'manualStep', step: { kind: 'freeRotate', heading: heading(45) } }, '/agvs/AGV-1/manual/step', { kind: 'freeRotate', heading: 45 }],
    [{ kind: 'manualCarrier', action: 'lower' }, '/agvs/AGV-1/manual/carrier', { action: 'lower' }],
  ] as const)('%j', (command, path, body) => {
    expect(agvCommandRequest(agv, command)).toEqual({ path, body });
  });

  it('ID は URL に使えるように直す', () => {
    expect(agvCommandRequest(agvId('AGV 1/x'), { kind: 'alarmClear' }).path).toBe('/agvs/AGV%201%2Fx/alarm-clear');
  });
});

describe('sendAgvCommand', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  const respond = (status: number, body?: unknown) =>
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(body === undefined ? null : JSON.stringify(body), { status })));

  it('受け付けられたら、サーバーが返した結果', async () => {
    respond(200, { result: 'reserved' });

    await expect(sendAgvCommand('', agv, { kind: 'alarmClear' }, 10_000)).resolves.toBe('reserved');
  });

  it('今の状態で受け付けられなかった（409）ときは refused', async () => {
    respond(409, { result: 'notAllowed' });

    await expect(sendAgvCommand('', agv, { kind: 'alarmClear' }, 10_000)).resolves.toBe('refused');
  });

  it('知らない結果、ほかの応答、応答がないときは failed（やり直さない）', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {});
    respond(200, { result: 'surprise' });
    await expect(sendAgvCommand('', agv, { kind: 'alarmClear' }, 10_000)).resolves.toBe('failed');

    respond(500);
    await expect(sendAgvCommand('', agv, { kind: 'alarmClear' }, 10_000)).resolves.toBe('failed');

    const fetch = vi.fn().mockRejectedValue(new DOMException('時間切れ', 'TimeoutError'));
    vi.stubGlobal('fetch', fetch);
    await expect(sendAgvCommand('', agv, { kind: 'alarmClear' }, 10_000)).resolves.toBe('failed');
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it('JSON を POST で送り、決めた時間であきらめる', async () => {
    const timeout = vi.spyOn(AbortSignal, 'timeout');
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ result: 'accepted' }), { status: 200 }));
    vi.stubGlobal('fetch', fetch);

    await sendAgvCommand('http://api', agv, { kind: 'manualCarrier', action: 'raise' }, 10_000);

    expect(fetch).toHaveBeenCalledWith(
      'http://api/agvs/AGV-1/manual/carrier',
      expect.objectContaining({ method: 'POST', body: JSON.stringify({ action: 'raise' }), headers: { 'Content-Type': 'application/json' } }),
    );
    expect(timeout).toHaveBeenCalledWith(10_000);
  });
});

describe('commandNotice（カードに出す知らせ）', () => {
  it('受け付けたときは、何も出さない', () => {
    expect(commandNotice('accepted')).toBeNull();
  });

  it.each(['reserved', 'refused', 'failed'] as const)('%s は、理由を出す', (result) => {
    expect(commandNotice(result)).not.toBeNull();
  });
});

describe('toManualOutcome', () => {
  it('マニュアル操作の結果の言葉にする', () => {
    expect(toManualOutcome('accepted')).toBe('accepted');
    expect(toManualOutcome('arrivalHeadingNotAllowed')).toBe('arrivalHeadingNotAllowed');
    expect(toManualOutcome('refused')).toBe('refused');
    expect(toManualOutcome('reserved')).toBe('failed'); // マニュアル操作では返らないはずのもの
  });
});
