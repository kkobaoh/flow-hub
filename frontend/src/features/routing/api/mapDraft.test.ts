import { afterEach, describe, expect, it, vi } from 'vitest';
import { heading, panelId } from '../model';
import type { MapDraft } from '../mapEdit';
import { carrierHeightNotice, saveCarrierHeight, loadMapDraft, mapPublishNotice, parseMapDraft, publishMapDraft, saveMapDraft, toWireDraft, VALIDATION_FAILED, validateMapDraft } from './mapDraft';

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
    expect(
      parseMapDraft({
        revision: 3,
        savedAt: '2026-11-25T09:00:00Z',
        publishedVersion: 2,
        draft: wireDraft,
        heights: [{ panel: 'D1', heightMm: 320, source: 'measured', setAt: '2026-11-24T08:00:00Z' }],
      }),
    ).toEqual({
      revision: 3,
      savedAt: new Date('2026-11-25T09:00:00Z'),
      publishedVersion: 2,
      draft,
      heights: { D1: { heightMm: 320, source: 'measured', at: new Date('2026-11-24T08:00:00Z') } },
    });
  });

  it('下書きも公開中の版もなければ、下書きは null', () => {
    expect(parseMapDraft({ revision: 0, savedAt: null, publishedVersion: 0, draft: null, heights: [] }).draft).toBeNull();
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
        .mockResolvedValueOnce(new Response(JSON.stringify({ revision: 1, savedAt: null, publishedVersion: 1, draft: wireDraft, heights: [] }), { status: 200 }))
        .mockRejectedValueOnce(new Error('切れた')),
    );
    vi.spyOn(console, 'warn').mockImplementation(() => {});

    expect(await loadMapDraft('', 1000)).toMatchObject({ revision: 1, draft });
    expect(await loadMapDraft('', 1000)).toBe('failed');
  });

  it('検証する。見つかった問題を返し、応答がなければ、検証できなかったことを問題として返す', async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(Response.json({ requestToken: 'csrf' }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ problems: ['P1 がつながっていません。'] }), { status: 200 }))
      .mockResolvedValueOnce(Response.json({ requestToken: 'csrf' }))
      .mockResolvedValueOnce(new Response(null, { status: 403 }))
      .mockRejectedValueOnce(new Error('切れた'));
    vi.stubGlobal('fetch', fetch);
    vi.spyOn(console, 'warn').mockImplementation(() => {});

    expect(await validateMapDraft('', draft, 1000)).toEqual(['P1 がつながっていません。']);
    expect((fetch.mock.calls[1] as [string, RequestInit])[0]).toBe('/map/draft/validate');
    expect(JSON.parse((fetch.mock.calls[1] as [string, RequestInit])[1].body as string)).toEqual({ draft: wireDraft });
    expect(await validateMapDraft('', draft, 1000)).toEqual(['マップを検証する権限がありません。']);
    expect(await validateMapDraft('', draft, 1000)).toEqual([VALIDATION_FAILED]);
  });

  it('キャリアの高さを入れる・空にする。断られたら理由を出す', async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(Response.json({ requestToken: 'csrf' }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ result: 'saved', height: { panel: 'D1', heightMm: 350, source: 'manual', setAt: '2026-11-26T09:00:00Z' } }), { status: 200 }))
      .mockResolvedValueOnce(Response.json({ requestToken: 'csrf' }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ result: 'saved', height: null }), { status: 200 }))
      .mockResolvedValueOnce(Response.json({ requestToken: 'csrf' }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ result: 'notTransferPanel', height: null }), { status: 200 }))
      .mockResolvedValueOnce(Response.json({ requestToken: 'csrf' }))
      .mockResolvedValueOnce(new Response(null, { status: 403 }));
    vi.stubGlobal('fetch', fetch);

    expect(await saveCarrierHeight('', 'D1', 350, 1000)).toEqual({ result: 'saved', height: { heightMm: 350, source: 'manual', at: new Date('2026-11-26T09:00:00Z') } });
    expect(fetch.mock.calls[1]).toEqual(['/map/carrier-heights/D1', expect.objectContaining({
      headers: expect.objectContaining({ 'X-FlowHub-CSRF': 'csrf' }),
    })]);
    expect(await saveCarrierHeight('', 'D1', null, 1000)).toEqual({ result: 'saved', height: null });
    expect(JSON.parse((fetch.mock.calls[3] as [string, RequestInit])[1].body as string)).toEqual({ heightMm: null });
    expect(carrierHeightNotice('P1', await saveCarrierHeight('', 'P1', 350, 1000))).toBe('P1 は、公開中の版の受け渡しパネルではないので、保存しませんでした。');
    expect(carrierHeightNotice('D1', await saveCarrierHeight('', 'D1', null, 1000))).toBe('キャリアの高さを変更する権限がありません。');
  });

  it('公開する。結果ごとに中身を返し、応答がなければ failed', async () => {
    const reply = (body: object) => new Response(JSON.stringify({ version: 0, problems: [], agvs: [], ...body }), { status: 200 });
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(Response.json({ requestToken: 'csrf' }))
      .mockResolvedValueOnce(reply({ result: 'published', version: 4, agvs: ['AGV-2'] }))
      .mockResolvedValueOnce(Response.json({ requestToken: 'csrf' }))
      .mockResolvedValueOnce(reply({ result: 'agvMoving', agvs: ['AGV-1'] }))
      .mockResolvedValueOnce(Response.json({ requestToken: 'csrf' }))
      .mockResolvedValueOnce(reply({ result: 'problems', problems: ['P1 がつながっていません。'] }))
      .mockResolvedValueOnce(Response.json({ requestToken: 'csrf' }))
      .mockResolvedValueOnce(new Response(null, { status: 403 }))
      .mockRejectedValueOnce(new Error('切れた'));
    vi.stubGlobal('fetch', fetch);
    vi.spyOn(console, 'warn').mockImplementation(() => {});

    expect(await publishMapDraft('', 3, draft, 1000)).toEqual({ result: 'published', version: 4, agvs: ['AGV-2'] });
    expect((fetch.mock.calls[1] as [string, RequestInit])[0]).toBe('/map/draft/publish');
    expect(JSON.parse((fetch.mock.calls[1] as [string, RequestInit])[1].body as string)).toEqual({ revision: 3, draft: wireDraft });
    const moving = await publishMapDraft('', 3, draft, 1000);
    expect(mapPublishNotice(moving)).toBe('AGV-1 が走っているので、公開しませんでした。止まってから、公開してください。');
    expect(mapPublishNotice(await publishMapDraft('', 3, draft, 1000))).toBe('公開しませんでした。P1 がつながっていません。');
    expect(mapPublishNotice(await publishMapDraft('', 3, draft, 1000))).toBe('マップを公開する権限がありません。');
    expect(await publishMapDraft('', 3, draft, 1000)).toEqual({ result: 'failed' });
  });

  it('保存する。古い番号なら stale、応答がなければ failed', async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(Response.json({ requestToken: 'csrf' }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ result: 'saved', revision: 4 }), { status: 200 }))
      .mockResolvedValueOnce(Response.json({ requestToken: 'csrf' }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ result: 'stale', revision: 5 }), { status: 200 }))
      .mockResolvedValueOnce(Response.json({ requestToken: 'csrf' }))
      .mockResolvedValueOnce(new Response(null, { status: 403 }))
      .mockRejectedValueOnce(new Error('切れた'));
    vi.stubGlobal('fetch', fetch);
    vi.spyOn(console, 'warn').mockImplementation(() => {});

    expect(await saveMapDraft('', 3, draft, 1000)).toEqual({ result: 'saved', revision: 4 });
    expect(fetch.mock.calls[1]).toEqual(['/map/draft', expect.objectContaining({
      headers: expect.objectContaining({ 'X-FlowHub-CSRF': 'csrf' }),
      body: JSON.stringify({ revision: 3, draft: wireDraft }),
    })]);
    expect(await saveMapDraft('', 3, draft, 1000)).toEqual({ result: 'stale', revision: 5 });
    expect(await saveMapDraft('', 3, draft, 1000)).toEqual({ result: 'forbidden' });
    expect(await saveMapDraft('', 3, draft, 1000)).toEqual({ result: 'failed' });
  });
});
