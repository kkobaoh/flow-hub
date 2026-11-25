import { afterEach, describe, expect, it, vi } from 'vitest';
import { consumableCommandRequest, consumableNotice, sendConsumableCommand } from './consumableCommands';

describe('consumableCommandRequest（消耗品の操作を、サーバーの窓口へ送る形にする）', () => {
  it('ストッカーの設定は、品種ごとの個数を送る', () => {
    expect(consumableCommandRequest({ kind: 'stockerSetting', device: 'DEV-1', counts: [{ variety: 'DR-5', count: 2 }] })).toEqual({
      path: '/devices/DEV-1/stocker-setting',
      body: { counts: [{ variety: 'DR-5', count: 2 }] },
    });
  });

  it('指名と、指名の取り消し', () => {
    expect(consumableCommandRequest({ kind: 'designate', consumable: 'T-1', device: 'DEV-1' })).toEqual({ path: '/consumables/T-1/designate', body: { device: 'DEV-1' } });
    expect(consumableCommandRequest({ kind: 'cancelDesignation', consumable: 'T-1' })).toEqual({ path: '/consumables/T-1/cancel-designation', body: {} });
  });

  it('出庫と、要確認を直す', () => {
    expect(consumableCommandRequest({ kind: 'ship', consumable: 'T-1' })).toEqual({ path: '/consumables/T-1/ship', body: {} });
    expect(consumableCommandRequest({ kind: 'correct', consumable: 'X-1', variety: 'DR-5', condition: '中古品' })).toEqual({
      path: '/consumables/X-1/correct',
      body: { variety: 'DR-5', condition: 'used' },
    });
  });

  it('ID は URL に使えるように直す', () => {
    expect(consumableCommandRequest({ kind: 'ship', consumable: 'T 1/x' }).path).toBe('/consumables/T%201%2Fx/ship');
  });
});

describe('sendConsumableCommand', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  const respond = (status: number, body?: unknown) =>
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(body === undefined ? null : JSON.stringify(body), { status })));

  it('サーバーが返した結果を返す', async () => {
    respond(200, { result: 'inAssignedTransport' });

    await expect(sendConsumableCommand('', { kind: 'ship', consumable: 'T-1' }, 10_000)).resolves.toBe('inAssignedTransport');
  });

  it('知らない結果、断られた、応答がないときは failed（やり直さない）', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {});
    respond(200, { result: 'surprise' });
    await expect(sendConsumableCommand('', { kind: 'ship', consumable: 'T-1' }, 10_000)).resolves.toBe('failed');

    respond(400, { result: 'invalid' });
    await expect(sendConsumableCommand('', { kind: 'ship', consumable: 'T-1' }, 10_000)).resolves.toBe('failed');
  });
});

describe('consumableNotice（断られたときに出す知らせ）', () => {
  it.each([
    ['stockerSetting', 'saved', null],
    ['ship', 'requested', null],
    ['correct', 'corrected', null],
    ['designate', 'designated', null],
    ['cancelDesignation', 'canceled', null],
    ['stockerSetting', 'exceedsSlots', '個数の合計がスロットの数を超えているので、保存できませんでした。'],
    ['ship', 'notAtStation', 'ステーションにないので、出庫できませんでした。表示を確かめてください。'],
    ['ship', 'inAssignedTransport', '届ける搬送に AGV を割り当て済みなので、出庫できませんでした。'],
    ['cancelDesignation', 'inAssignedTransport', '届ける搬送に AGV を割り当て済みなので、指名を取り消せませんでした。'],
    ['correct', 'notNeedingCheck', 'もう要確認ではないので、直しませんでした（ほかの人が直したか、出庫したなど）。表示を確かめてください。'],
    ['designate', 'notDesignatable', '指名できませんでした。指名できるのは、ステーションにある、要確認でない新品で、届ける予約も出庫の頼みも指名もないものです。表示を確かめてください。'],
    ['ship', 'failed', 'サーバーから応答がなく、届いたかわかりません。表示を確かめてください。'],
  ] as const)('%s の %s', (kind, result, notice) => {
    expect(consumableNotice(kind, result)).toBe(notice);
  });
});
