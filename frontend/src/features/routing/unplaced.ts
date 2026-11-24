import type { Point, Rect } from './drawing';

/** 位置がわからない AGV の列（マップの下の外に、横一列に並べる）。 */
export interface UnplacedRow {
  /** 見出し「位置がわからない AGV」を書く左上の位置（mm）。 */
  readonly title: Point;
  /** 見出しの字の大きさ（mm）。 */
  readonly titleFontMm: number;
  /** AGV の印の中心（mm）。左から順に。 */
  readonly slots: readonly Point[];
  /** マップと列をあわせて囲む範囲（mm）。いなければマップの範囲のまま。 */
  readonly bounds: Rect;
}

// パネルの一辺に対する割合
const TITLE_GAP = 0.45;
const TITLE_FONT = 0.2;
const SLOT_GAP = 0.6;
const SLOT_PITCH = 0.9;
const SLOT_RADIUS = 0.28; // MapCanvas の印の大きさと同じ
const BOTTOM_MARGIN = 0.2;

/**
 * 位置がわからない AGV（マップにないパネルにいるものも含む）を並べる列の置き方を決める。
 * マップの一番下の外に見出しを置き、その下に左から横一列に並べる。
 * @param content マップに描くもの（パネルと装置の外形）を囲む範囲。
 * @param count 並べる AGV の数。
 */
export function unplacedRow(content: Rect, panelSizeMm: number, count: number): UnplacedRow {
  const titleFontMm = panelSizeMm * TITLE_FONT;
  const bottom = content.y + content.height;
  const title = { x: content.x, y: bottom + panelSizeMm * TITLE_GAP };
  if (count === 0) {
    return { title, titleFontMm, slots: [], bounds: content };
  }
  const radius = panelSizeMm * SLOT_RADIUS;
  const y = title.y + titleFontMm + panelSizeMm * SLOT_GAP;
  const slots = Array.from({ length: count }, (_, i) => ({ x: content.x + radius + i * panelSizeMm * SLOT_PITCH, y }));
  const right = Math.max(content.x + content.width, slots[slots.length - 1]!.x + radius * 1.4);
  const lowest = y + radius * 1.4 + panelSizeMm * BOTTOM_MARGIN;
  return { title, titleFontMm, slots, bounds: { x: content.x, y: content.y, width: right - content.x, height: lowest - content.y } };
}

// 印の名前の字の大きさ（パネルの一辺に対する割合）。名前は ID そのままなので、長ければ円に収まるように小さくする
const LABEL_FONT = 0.22;
const LABEL_MAX_WIDTH = 0.48;
const CHAR_WIDTH_EM = 0.6;

/** AGV の印に書く名前の字の大きさ（パネルの一辺に対する割合）。 */
export function agvLabelFontRatio(label: string): number {
  return Math.min(LABEL_FONT, LABEL_MAX_WIDTH / (Math.max(label.length, 1) * CHAR_WIDTH_EM));
}
