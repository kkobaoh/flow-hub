import { afterEach, describe, expect, it, vi } from 'vitest';
import { agvRegistrationNotice, agvRegistrationRequest, agvTypeCommandRequest, agvTypeNotice, parseFleet, sendAgvTypeCommand } from './fleetSnapshot';

const wire = {
  version: 2,
  types: [{ id: 'small', name: '小さい', forward: 500, backward: 250, rotation: 90, chargeAt: 20, resumeAt: 80, special: false }],
  agvs: [
    { id: 'AGV-1', type: 'small', carrier: 'X', control: 'detached', registration: 'both' },
    { id: 'AGV-2', type: null, carrier: 'X', control: null, registration: 'dispatchOnly' },
  ],
  defaults: { chargeAt: 20, resumeAt: 80 },
  carriers: ['X', 'Y'],
};

describe('parseFleet', () => {
  it('サーバーから届いた AGV と AGV の種類の登録を、型にする', () => {
    expect(parseFleet(wire)).toEqual({
      version: 2,
      types: wire.types,
      entries: [
        { id: 'AGV-1', type: 'small', carrier: 'X', control: 'detached', registration: 'both' },
        { id: 'AGV-2', type: null, carrier: 'X', control: null, registration: 'dispatchOnly' },
      ],
      defaults: { chargeAt: 20, resumeAt: 80 },
      carriers: ['X', 'Y'],
    });
  });

  it('自動で登録して種類が未定の AGV も読む', () => {
    const parsed = parseFleet({ ...wire, agvs: [{ id: 'AGV-9', type: null, carrier: null, control: null, registration: 'unconfigured' }] });

    expect(parsed.entries).toEqual([{ id: 'AGV-9', type: null, carrier: null, control: null, registration: 'unconfigured' }]);
  });

  it.each([
    ['種類の並びがない', { ...wire, types: undefined }],
    ['速さが数でない', { ...wire, types: [{ ...wire.types[0], forward: '速い' }] }],
    ['知らないコントロールステータス', { ...wire, agvs: [{ ...wire.agvs[0], control: 'flying' }] }],
    ['知らない登録', { ...wire, agvs: [{ ...wire.agvs[0], registration: 'maybe' }] }],
    ['既定値がない', { ...wire, defaults: null }],
  ])('形が正しくなければ、例外にする（%s）', (_, value) => {
    expect(() => parseFleet(value)).toThrow();
  });
});

const type = { id: 'small', name: '小さい', forward: 500, backward: 250, rotation: 90, chargeAt: 20, resumeAt: 80, special: true };

describe('agvTypeCommandRequest', () => {
  it('加える・変える・消す', () => {
    expect(agvTypeCommandRequest({ kind: 'add', type })).toEqual({ path: '/agv-types', body: type });
    expect(agvTypeCommandRequest({ kind: 'edit', type })).toEqual({ path: '/agv-types/small/edit', body: type });
    expect(agvTypeCommandRequest({ kind: 'delete', id: 'small' })).toEqual({ path: '/agv-types/small/delete', body: {} });
  });
});

describe('sendAgvTypeCommand', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('サーバーが返した結果を返し、知らない結果や応答がなければ failed', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ result: 'nameTaken' }), { status: 200 })));
    await expect(sendAgvTypeCommand('', { kind: 'add', type }, 10_000)).resolves.toBe('nameTaken');

    vi.spyOn(console, 'warn').mockImplementation(() => {});
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 400 })));
    await expect(sendAgvTypeCommand('', { kind: 'add', type }, 10_000)).resolves.toBe('failed');
  });
});

describe('agvRegistrationRequest', () => {
  it('加える・変える・そろえる・消す', () => {
    expect(agvRegistrationRequest({ kind: 'add', id: 'AGV-5', type: 'small', carrier: 'X' })).toEqual({ path: '/agv-registrations', body: { id: 'AGV-5', type: 'small', carrier: 'X' } });
    expect(agvRegistrationRequest({ kind: 'change', id: 'AGV-5', type: 'small', carrier: 'X' })).toEqual({ path: '/agv-registrations/AGV-5/edit', body: { type: 'small', carrier: 'X' } });
    expect(agvRegistrationRequest({ kind: 'align', id: 'AGV-5', type: null, carrier: 'X' })).toEqual({ path: '/agv-registrations/AGV-5/align', body: { type: null, carrier: 'X' } });
    expect(agvRegistrationRequest({ kind: 'delete', id: 'AGV 5' })).toEqual({ path: '/agv-registrations/AGV%205/delete', body: {} });
  });
});

describe('agvRegistrationNotice', () => {
  it.each([
    ['saved', null],
    ['deleted', null],
    ['idTaken', 'その番号の AGV は、もう登録してあります。'],
    ['notDetached', '切り離しでないので、変えたり消したりできませんでした。'],
    ['loaded', 'ものを積んだまま止まっているので、消せませんでした。積んでいるものを届けるか返してから消してください。'],
  ] as const)('%s', (result, notice) => {
    expect(agvRegistrationNotice(result)).toBe(notice);
  });
});

describe('agvTypeNotice', () => {
  it.each([
    ['saved', null],
    ['deleted', null],
    ['idTaken', 'その ID の種類は、もう登録してあります。'],
    ['nameTaken', 'その名前の種類は、もう登録してあります。'],
    ['notFound', 'その種類は、もうありません。表示を確かめてください。'],
    ['inUse', 'その種類を使う AGV があるので、消せませんでした。'],
    ['failed', '保存できませんでした。サーバーから応答がないか、走行と配車の片方に書けませんでした。表示を確かめてください。'],
  ] as const)('%s', (result, notice) => {
    expect(agvTypeNotice(result)).toBe(notice);
  });
});
