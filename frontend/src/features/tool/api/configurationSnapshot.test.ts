import { afterEach, describe, expect, it, vi } from 'vitest';
import { configurationNotice, configurationRequest, parseConfiguration, sendConfigurationCommand } from './configurationSnapshot';

const wire = {
  version: 3,
  processTypes: [{ id: 'drill', carrier: 'X', capacity: 2 }],
  varieties: [{ id: 'DR-5', processType: 'drill', expected: ['T-5'], stocked: ['T-1'], inUse: true }],
  devices: [{ id: 'DEV-1', panel: 'D1', slots: [2, 3], occupied: [{ stocker: 2, slot: 3 }, { stocker: 2, slot: 1 }], inUse: true }],
  stations: [{ id: 'ST-1', panel: 'ST', inUse: false }],
};

describe('parseConfiguration', () => {
  it('サーバーから届いた消耗品の構成を、型にする（入庫の予定と在庫にある ID を、対応させた ID にまとめる）', () => {
    expect(parseConfiguration(wire)).toEqual({
      version: 3,
      processKinds: [{ name: 'drill', carrier: 'X', capacity: 2 }],
      varieties: [{ name: 'DR-5', processKind: 'drill', ids: ['T-5', 'T-1'], stockedIds: ['T-1'], inUse: true }],
      devices: [{ id: 'DEV-1', panel: 'D1', slots: [2, 3], occupied: [[], [3, 1]], busy: true }],
      stations: [{ id: 'ST-1', panel: 'ST', busy: false }],
    });
  });

  it.each([
    ['加工の種類の並びがない', { ...wire, processTypes: undefined }],
    ['個数が 0', { ...wire, processTypes: [{ id: 'drill', carrier: 'X', capacity: 0 }] }],
    ['キャリアの種類が空', { ...wire, processTypes: [{ id: 'drill', carrier: '', capacity: 2 }] }],
    ['使われているかがない', { ...wire, varieties: [{ ...wire.varieties[0], inUse: undefined }] }],
    ['入庫の予定が並びでない', { ...wire, varieties: [{ ...wire.varieties[0], expected: 'T-5' }] }],
    ['番号がない', { ...wire, version: undefined }],
    ['ストッカーがない', { ...wire, devices: [{ ...wire.devices[0], slots: [] }] }],
    ['ないストッカーのスロット', { ...wire, devices: [{ ...wire.devices[0], occupied: [{ stocker: 3, slot: 1 }] }] }],
    ['ステーションの使われているかがない', { ...wire, stations: [{ id: 'ST-1', panel: 'ST' }] }],
  ])('形が正しくなければ、例外にする（%s）', (_, value) => {
    expect(() => parseConfiguration(value)).toThrow();
  });
});

describe('configurationRequest', () => {
  const kind = { name: 'drill', carrier: 'X', capacity: 2 };

  it('加工の種類を加える・変える・消す', () => {
    expect(configurationRequest({ kind: 'addProcessKind', processKind: kind })).toEqual({ path: '/configuration/process-types', body: { id: 'drill', carrier: 'X', capacity: 2 } });
    expect(configurationRequest({ kind: 'editProcessKind', processKind: kind })).toEqual({ path: '/configuration/process-types/drill/edit', body: { carrier: 'X', capacity: 2 } });
    expect(configurationRequest({ kind: 'deleteProcessKind', name: 'drill' })).toEqual({ path: '/configuration/process-types/drill/delete', body: {} });
  });

  it('品種を加える・変える・消す', () => {
    expect(configurationRequest({ kind: 'addVariety', name: 'DR-5', processKind: 'drill' })).toEqual({ path: '/configuration/varieties', body: { id: 'DR-5', processType: 'drill' } });
    expect(configurationRequest({ kind: 'editVariety', name: 'DR-5', processKind: 'drill' })).toEqual({ path: '/configuration/varieties/DR-5/edit', body: { processType: 'drill' } });
    expect(configurationRequest({ kind: 'deleteVariety', name: 'DR 5' })).toEqual({ path: '/configuration/varieties/DR%205/delete', body: {} });
  });

  it('入庫の予定を加える・外す', () => {
    expect(configurationRequest({ kind: 'addId', variety: 'DR-5', id: 'T-5' })).toEqual({ path: '/configuration/varieties/DR-5/expected-arrivals', body: { consumable: 'T-5' } });
    expect(configurationRequest({ kind: 'removeId', id: 'T/5' })).toEqual({ path: '/configuration/expected-arrivals/T%2F5/delete', body: {} });
  });
});

describe('configurationRequest（装置とステーション）', () => {
  it('装置を加える・変える・消す', () => {
    const device = { id: 'DEV-2', panel: 'D2', slots: [2, 3] };
    expect(configurationRequest({ kind: 'addDevice', device })).toEqual({ path: '/configuration/devices', body: { id: 'DEV-2', panel: 'D2', slots: [2, 3] } });
    expect(configurationRequest({ kind: 'editDevice', device })).toEqual({ path: '/configuration/devices/DEV-2/edit', body: { panel: 'D2', slots: [2, 3] } });
    expect(configurationRequest({ kind: 'deleteDevice', id: 'DEV-2' })).toEqual({ path: '/configuration/devices/DEV-2/delete', body: {} });
  });

  it('ステーションを加える・変える・消す', () => {
    const station = { id: 'ST-1', panel: 'ST' };
    expect(configurationRequest({ kind: 'addStation', station })).toEqual({ path: '/configuration/stations', body: { id: 'ST-1', panel: 'ST' } });
    expect(configurationRequest({ kind: 'editStation', station })).toEqual({ path: '/configuration/stations/ST-1/edit', body: { panel: 'ST' } });
    expect(configurationRequest({ kind: 'deleteStation', id: 'ST-1' })).toEqual({ path: '/configuration/stations/ST-1/delete', body: {} });
  });
});

describe('sendConfigurationCommand', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('サーバーの結果を返し、応答がなければ failed にする', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(Response.json({ requestToken: 'csrf' }))
      .mockResolvedValueOnce(Response.json({ result: 'inUse' }))
      .mockResolvedValueOnce(Response.json({ requestToken: 'csrf' }))
      .mockResolvedValueOnce(new Response(null, { status: 403 }))
      .mockRejectedValueOnce(new Error('切れた'));
    vi.stubGlobal('fetch', fetch);
    vi.spyOn(console, 'warn').mockImplementation(() => {});

    expect(await sendConfigurationCommand('', { kind: 'deleteVariety', name: 'DR-5' }, 1000)).toBe('inUse');
    expect(fetch).toHaveBeenLastCalledWith('/configuration/varieties/DR-5/delete', expect.objectContaining({
      headers: expect.objectContaining({ 'X-FlowHub-CSRF': 'csrf' }),
    }));
    const command = { kind: 'deleteVariety', name: 'DR-5' } as const;
    expect(configurationNotice(command, await sendConfigurationCommand('', command, 1000))).toBe('消耗品の構成を変更する権限がありません。');
    expect(await sendConfigurationCommand('', { kind: 'deleteVariety', name: 'DR-5' }, 1000)).toBe('failed');
  });
});

describe('configurationNotice', () => {
  it('受け付けたら知らせない。断られたら、何を断られたかを出す', () => {
    expect(configurationNotice({ kind: 'deleteVariety', name: 'DR-5' }, 'saved')).toBeNull();
    expect(configurationNotice({ kind: 'addVariety', name: 'DR-5', processKind: 'drill' }, 'alreadyRegistered')).toBe('その名前の品種は、もう登録してあります。');
    expect(configurationNotice({ kind: 'addId', variety: 'DR-5', id: 'T-1' }, 'alreadyRegistered')).toBe('T-1 は、もうほかの品種に対応させてあるか、消耗品の記録にあります。');
    expect(configurationNotice({ kind: 'deleteProcessKind', name: 'drill' }, 'inUse')).toBe('その加工の種類の品種があるので、消せませんでした。');
    expect(configurationNotice({ kind: 'editVariety', name: 'DR-5', processKind: 'mill' }, 'inUse')).toBe('その品種は、消耗品の記録かストッカーの設定で使っているので、変えられませんでした。');
    expect(configurationNotice({ kind: 'removeId', id: 'T-1' }, 'notFound')).toBe('入庫の予定が見つかりませんでした。ほかの画面で変えられたかもしれません。表示を確かめてください。');
    expect(configurationNotice({ kind: 'removeId', id: 'T-1' }, 'failed')).toBe('保存できませんでした。サーバーから応答がありません。表示を確かめてください。');
    expect(configurationNotice({ kind: 'deleteDevice', id: 'DEV-1' }, 'inUse')).toBe('その装置は、中に消耗品があるか、かかわる搬送があるので、消せませんでした。');
    expect(configurationNotice({ kind: 'editStation', station: { id: 'ST-1', panel: 'D2' } }, 'inUse')).toBe(
      'そのステーションは、中に消耗品があるか、かかわる搬送があるので、受け渡しパネルを変えられませんでした。',
    );
    expect(configurationNotice({ kind: 'addStation', station: { id: 'ST-2', panel: 'D2' } }, 'stationLimit')).toBe('ステーションは、1台だけ登録できます（ステーションの選び方のルールが決まるまで）。');
    expect(configurationNotice({ kind: 'editDevice', device: { id: 'DEV-1', panel: 'D1', slots: [1] } }, 'exceedsStockerSetting')).toBe(
      'ストッカーの設定の個数の合計より、スロットが少なくなるので、減らせませんでした。先にストッカーの設定を減らしてください。',
    );
  });
});
