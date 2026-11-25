import { afterEach, describe, expect, it, vi } from 'vitest';
import { configurationNotice, configurationRequest, parseConfiguration, sendConfigurationCommand } from './configurationSnapshot';

const wire = {
  version: 3,
  processTypes: [{ id: 'drill', carrier: 'X', capacity: 2 }],
  varieties: [{ id: 'DR-5', processType: 'drill', expected: ['T-5'], stocked: ['T-1'], inUse: true }],
};

describe('parseConfiguration', () => {
  it('サーバーから届いた消耗品の構成を、型にする（入庫の予定と在庫にある ID を、対応させた ID にまとめる）', () => {
    expect(parseConfiguration(wire)).toEqual({
      version: 3,
      processKinds: [{ name: 'drill', carrier: 'X', capacity: 2 }],
      varieties: [{ name: 'DR-5', processKind: 'drill', ids: ['T-5', 'T-1'], stockedIds: ['T-1'], inUse: true }],
    });
  });

  it.each([
    ['加工の種類の並びがない', { ...wire, processTypes: undefined }],
    ['個数が 0', { ...wire, processTypes: [{ id: 'drill', carrier: 'X', capacity: 0 }] }],
    ['キャリアの種類が空', { ...wire, processTypes: [{ id: 'drill', carrier: '', capacity: 2 }] }],
    ['使われているかがない', { ...wire, varieties: [{ ...wire.varieties[0], inUse: undefined }] }],
    ['入庫の予定が並びでない', { ...wire, varieties: [{ ...wire.varieties[0], expected: 'T-5' }] }],
    ['番号がない', { ...wire, version: undefined }],
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

describe('sendConfigurationCommand', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('サーバーの結果を返し、応答がなければ failed にする', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({ result: 'inUse' }), { status: 200 })).mockRejectedValueOnce(new Error('切れた')));
    vi.spyOn(console, 'warn').mockImplementation(() => {});

    expect(await sendConfigurationCommand('', { kind: 'deleteVariety', name: 'DR-5' }, 1000)).toBe('inUse');
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
  });
});
