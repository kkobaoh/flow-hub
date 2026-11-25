import { afterEach, describe, expect, it, vi } from 'vitest';
import { heading, panelId } from '../model';
import type { MapDraft } from '../mapEdit';
import { loadMapDraft, parseMapDraft, saveMapDraft, toWireDraft, VALIDATION_FAILED, validateMapDraft } from './mapDraft';

const wireDraft = {
  panelSizeMm: 1000,
  panels: [
    { id: 'P1', type: 'Normal', hasChargingPin: false, carrierLoweringHeading: null, column: 0, row: 0, crossOffsetEastMm: 0, crossOffsetSouthMm: 0, outline: null },
    {
      id: 'SP',
      type: 'SpecialTransfer',
      hasChargingPin: false,
      carrierLoweringHeading: 90,
      column: 1,
      row: 0,
      crossOffsetEastMm: 5,
      crossOffsetSouthMm: -3,
      outline: { xMm: 1500, yMm: -800, widthMm: 2000, heightMm: 1600 },
    },
  ],
  connections: [{ from: 'P1', to: 'SP', distanceMm: 1000, heading: 90, oneWay: false }],
};

const draft: MapDraft = {
  layout: {
    panelSizeMm: 1000,
    panels: [
      { id: panelId('P1'), type: 'Normal', hasChargingPin: false, column: 0, row: 0, crossOffsetMm: { x: 0, y: 0 } },
      { id: panelId('SP'), type: 'SpecialTransfer', hasChargingPin: false, carrierLoweringHeading: heading(90), column: 1, row: 0, crossOffsetMm: { x: 5, y: -3 } },
    ],
    connections: [{ from: panelId('P1'), to: panelId('SP'), distanceMm: 1000, heading: heading(90), oneWay: false }],
  },
  outlines: { SP: { x: 1500, y: -800, width: 2000, height: 1600 } },
};

describe('parseMapDraft', () => {
  it('サーバーの下書きを、画面の下書きにする（外形は受け渡しパネルごと）', () => {
    expect(parseMapDraft({ revision: 3, savedAt: '2026-11-25T09:00:00Z', publishedVersion: 2, draft: wireDraft })).toEqual({
      revision: 3,
      savedAt: new Date('2026-11-25T09:00:00Z'),
      publishedVersion: 2,
      draft,
    });
  });

  it('下書きも公開中の版もなければ、下書きは null', () => {
    expect(parseMapDraft({ revision: 0, savedAt: null, publishedVersion: 0, draft: null }).draft).toBeNull();
  });

  it.each([
    ['番号がない', { savedAt: null, publishedVersion: 1, draft: wireDraft }],
    ['知らない種類のパネル', { revision: 1, savedAt: null, publishedVersion: 1, draft: { ...wireDraft, panels: [{ ...wireDraft.panels[0], type: 'Wall' }] } }],
    ['つながりが並びでない', { revision: 1, savedAt: null, publishedVersion: 1, draft: { ...wireDraft, connections: {} } }],
  ])('形が正しくなければ、例外にする（%s）', (_, value) => {
    expect(() => parseMapDraft(value)).toThrow();
  });
});

describe('toWireDraft', () => {
  it('画面の下書きを、サーバーへ送る形にする', () => {
    expect(toWireDraft(draft)).toEqual(wireDraft);
  });
});

describe('loadMapDraft と saveMapDraft', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('読む。応答がなければ failed', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValueOnce(new Response(JSON.stringify({ revision: 1, savedAt: null, publishedVersion: 1, draft: wireDraft }), { status: 200 }))
        .mockRejectedValueOnce(new Error('切れた')),
    );
    vi.spyOn(console, 'warn').mockImplementation(() => {});

    expect(await loadMapDraft('', 1000)).toMatchObject({ revision: 1, draft });
    expect(await loadMapDraft('', 1000)).toBe('failed');
  });

  it('検証する。見つかった問題を返し、応答がなければ、検証できなかったことを問題として返す', async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ problems: ['P1 がつながっていません。'] }), { status: 200 }))
      .mockRejectedValueOnce(new Error('切れた'));
    vi.stubGlobal('fetch', fetch);
    vi.spyOn(console, 'warn').mockImplementation(() => {});

    expect(await validateMapDraft('', draft, 1000)).toEqual(['P1 がつながっていません。']);
    expect((fetch.mock.calls[0] as [string, RequestInit])[0]).toBe('/map/draft/validate');
    expect(JSON.parse((fetch.mock.calls[0] as [string, RequestInit])[1].body as string)).toEqual({ draft: wireDraft });
    expect(await validateMapDraft('', draft, 1000)).toEqual([VALIDATION_FAILED]);
  });

  it('保存する。古い番号なら stale、応答がなければ failed', async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ result: 'saved', revision: 4 }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ result: 'stale', revision: 5 }), { status: 200 }))
      .mockRejectedValueOnce(new Error('切れた'));
    vi.stubGlobal('fetch', fetch);
    vi.spyOn(console, 'warn').mockImplementation(() => {});

    expect(await saveMapDraft('', 3, draft, 1000)).toEqual({ result: 'saved', revision: 4 });
    expect(JSON.parse((fetch.mock.calls[0] as [string, RequestInit])[1].body as string)).toEqual({ revision: 3, draft: wireDraft });
    expect(await saveMapDraft('', 3, draft, 1000)).toEqual({ result: 'stale', revision: 5 });
    expect(await saveMapDraft('', 3, draft, 1000)).toEqual({ result: 'failed' });
  });
});
