import type { Rect } from '../drawing';
import type { MapDraft } from '../mapEdit';
import { heading, panelId, type Connection, type Panel, type PanelType } from '../model';

/** サーバーから読んだ下書き。 */
export interface MapDraftLoad {
  /** 下書きの番号（保存するときに渡す）。 */
  readonly revision: number;
  /** 下書きを保存した日時。下書きがなく、公開中の版の写しなら null。 */
  readonly savedAt: Date | null;
  /** 公開中の版の番号（まだ公開していなければ 0）。 */
  readonly publishedVersion: number;
  /** 下書き（なければ公開中の版の写し）。どちらもなければ null。 */
  readonly draft: MapDraft | null;
}

/** 下書きを保存した結果。stale は、読んだあとにほかの人が保存したか公開した（保存していない）。 */
export type MapDraftSaveOutcome = { readonly result: 'saved' | 'stale'; readonly revision: number } | { readonly result: 'failed' };

const PANEL_TYPES: ReadonlySet<string> = new Set<PanelType>(['Normal', 'Transfer', 'Standby', 'SpecialTransfer']);

const isRecord = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null;

function int(value: unknown, what: string): number {
  if (typeof value !== 'number' || !Number.isInteger(value)) {
    throw new TypeError(`${what}が整数ではありません（${String(value)}）。`);
  }
  return value;
}

function text(value: unknown, what: string): string {
  if (typeof value !== 'string' || value.trim() === '') {
    throw new TypeError(`${what}がありません。`);
  }
  return value;
}

function records(value: unknown, what: string): readonly Record<string, unknown>[] {
  if (!Array.isArray(value) || !value.every(isRecord)) {
    throw new TypeError(`${what}の並びが正しくありません。`);
  }
  return value;
}

/** サーバーから届いた下書きを検証して、画面の下書きにする（下書きは検証しないので、形だけを確かめる）。外形は、受け渡しパネルの ID をキーにする。 */
export function parseMapDraft(value: unknown): MapDraftLoad {
  if (!isRecord(value)) {
    throw new TypeError('マップの下書きの形が正しくありません。');
  }
  const savedAt = value.savedAt === null ? null : new Date(text(value.savedAt, '下書きを保存した日時'));
  if (savedAt !== null && Number.isNaN(savedAt.getTime())) {
    throw new TypeError('下書きを保存した日時が正しくありません。');
  }
  return {
    revision: int(value.revision, '下書きの番号'),
    savedAt,
    publishedVersion: int(value.publishedVersion, '公開中の版の番号'),
    draft: value.draft === null ? null : parseDraft(value.draft),
  };
}

function parseDraft(value: unknown): MapDraft {
  if (!isRecord(value)) {
    throw new TypeError('下書きの形が正しくありません。');
  }
  const outlines: Record<string, Rect> = {};
  const panels = records(value.panels, 'パネル').map((p): Panel => {
    const id = text(p.id, 'パネルの ID');
    const type = text(p.type, `${id} の種類`);
    if (!PANEL_TYPES.has(type)) {
      throw new TypeError(`${id} の種類がわかりません（${type}）。`);
    }
    if (typeof p.hasChargingPin !== 'boolean') {
      throw new TypeError(`${id} の充電ピンが正しくありません。`);
    }
    if (isRecord(p.outline)) {
      outlines[id] = {
        x: int(p.outline.xMm, `${id} の外形の左`),
        y: int(p.outline.yMm, `${id} の外形の上`),
        width: int(p.outline.widthMm, `${id} の外形の幅`),
        height: int(p.outline.heightMm, `${id} の外形の奥行き`),
      };
    }
    return {
      id: panelId(id),
      type: type as PanelType,
      hasChargingPin: p.hasChargingPin,
      ...(p.carrierLoweringHeading === null ? {} : { carrierLoweringHeading: heading(int(p.carrierLoweringHeading, `${id} のキャリアを下ろす角度`)) }),
      column: int(p.column, `${id} の列`),
      row: int(p.row, `${id} の行`),
      crossOffsetMm: { x: int(p.crossOffsetEastMm, `${id} の十字のずれ`), y: int(p.crossOffsetSouthMm, `${id} の十字のずれ`) },
    };
  });
  const connections = records(value.connections, 'つながり').map(
    (c): Connection => ({
      from: panelId(text(c.from, 'つながりの元')),
      to: panelId(text(c.to, 'つながりの先')),
      distanceMm: int(c.distanceMm, 'つながりの距離'),
      heading: heading(int(c.heading, 'つながりの向き')),
      oneWay: c.oneWay === true,
    }),
  );
  return { layout: { panelSizeMm: int(value.panelSizeMm, 'パネルの一辺'), panels, connections }, outlines };
}

/** 画面の下書きを、サーバーへ送る形にする。 */
export function toWireDraft(draft: MapDraft): Record<string, unknown> {
  return {
    panelSizeMm: draft.layout.panelSizeMm,
    panels: draft.layout.panels.map((p) => {
      const outline = draft.outlines[p.id];
      return {
        id: p.id,
        type: p.type,
        hasChargingPin: p.hasChargingPin,
        carrierLoweringHeading: p.carrierLoweringHeading ?? null,
        column: p.column,
        row: p.row,
        crossOffsetEastMm: p.crossOffsetMm.x,
        crossOffsetSouthMm: p.crossOffsetMm.y,
        outline: outline === undefined ? null : { xMm: outline.x, yMm: outline.y, widthMm: outline.width, heightMm: outline.height },
      };
    }),
    connections: draft.layout.connections.map((c) => ({ from: c.from, to: c.to, distanceMm: c.distanceMm, heading: c.heading, oneWay: c.oneWay })),
  };
}

/** 下書きを読む。timeoutMs を過ぎても応答がないか、形が正しくなければ failed（やり直さない）。 */
export async function loadMapDraft(baseUrl: string, timeoutMs: number): Promise<MapDraftLoad | 'failed'> {
  try {
    const response = await fetch(`${baseUrl}/map/draft`, { signal: AbortSignal.timeout(timeoutMs) });
    if (!response.ok) {
      console.warn(`マップの下書きを読めませんでした（${response.status}）。`);
      return 'failed';
    }
    return parseMapDraft(await response.json());
  } catch (error) {
    console.warn('マップの下書きを読めませんでした。', error);
    return 'failed';
  }
}

const SAVE_RESULTS: ReadonlySet<'saved' | 'stale'> = new Set(['saved', 'stale'] as const);

/** 下書きを保存する（読んだときの番号を渡す）。応答がなければ failed（やり直さない）。 */
export async function saveMapDraft(baseUrl: string, revision: number, draft: MapDraft, timeoutMs: number): Promise<MapDraftSaveOutcome> {
  try {
    const response = await fetch(`${baseUrl}/map/draft`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ revision, draft: toWireDraft(draft) }),
      signal: AbortSignal.timeout(timeoutMs),
    });
    const body: unknown = response.ok ? await response.json() : null;
    if (isRecord(body) && typeof body.result === 'string' && (SAVE_RESULTS as ReadonlySet<string>).has(body.result) && typeof body.revision === 'number') {
      return { result: body.result as 'saved' | 'stale', revision: body.revision };
    }
    console.warn(`マップの下書きを保存できませんでした（${response.status}）。`);
    return { result: 'failed' };
  } catch (error) {
    console.warn('マップの下書きを保存できませんでした。', error);
    return { result: 'failed' };
  }
}

/** 検証できなかったとき（応答がない、思わない応答）に、問題の代わりに出す文。公開できないようにするため、問題として扱う。 */
export const VALIDATION_FAILED = '検証できませんでした。サーバーから応答がありません。もう一度確かめてください。';

/** 下書きをサーバーで検証し、見つかった問題を返す（保存はしない）。検証できなければ、そのことを問題として返す（やり直さない）。 */
export async function validateMapDraft(baseUrl: string, draft: MapDraft, timeoutMs: number): Promise<readonly string[]> {
  try {
    const response = await fetch(`${baseUrl}/map/draft/validate`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ draft: toWireDraft(draft) }),
      signal: AbortSignal.timeout(timeoutMs),
    });
    const body: unknown = response.ok ? await response.json() : null;
    if (isRecord(body) && Array.isArray(body.problems) && body.problems.every((p) => typeof p === 'string')) {
      return body.problems as string[];
    }
    console.warn(`マップの下書きを検証できませんでした（${response.status}）。`);
    return [VALIDATION_FAILED];
  } catch (error) {
    console.warn('マップの下書きを検証できませんでした。', error);
    return [VALIDATION_FAILED];
  }
}

/** 画面に出す、下書きの保存の結果の知らせ（保存できたら null）。 */
export function mapDraftNotice(outcome: MapDraftSaveOutcome): string | null {
  switch (outcome.result) {
    case 'saved':
      return null;
    case 'stale':
      return '読んだあとに、ほかの人が下書きを保存したか公開したので、保存しませんでした。「サーバーから読み直す」で読み直してください。';
    case 'failed':
      return '下書きを保存できませんでした。サーバーから応答がありません。もう一度保存してください。';
    default:
      throw new Error(`知らない結果です: ${JSON.stringify(outcome satisfies never)}`);
  }
}

