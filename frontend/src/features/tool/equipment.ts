import type { Rect } from '../routing/drawing';
import type { PanelId } from '../routing/model';

declare const equipmentIdBrand: unique symbol;

/** 装置かステーションの ID。空でない文字列。 */
export type EquipmentId = string & { readonly [equipmentIdBrand]: true };

export function equipmentId(value: string): EquipmentId {
  if (value.trim() === '') {
    throw new RangeError('装置かステーションの ID が空です。');
  }
  return value as EquipmentId;
}

/** 気をつけることの種類。装置は不足・回収待ち・ID の食い違い・段取り替えのエラー、ステーションは要確認・補充待ち。 */
export type AttentionKind = 'shortage' | 'collectWaiting' | 'idMismatch' | 'setupError' | 'needsCheck' | 'replenishWaiting';

export interface Attention {
  readonly kind: AttentionKind;
  /** 数（消耗品の個数など）。数で表さないものは 1。 */
  readonly count: number;
}

/** マップの上に描く装置かステーション（床の下、受け渡しパネルの真下にある）。 */
export interface EquipmentOnMap {
  readonly id: EquipmentId;
  readonly kind: 'device' | 'station';
  /** 受け渡しパネル。 */
  readonly panel: PanelId;
  /** 外形（mm。マップと同じ座標）。 */
  readonly outlineMm: Rect;
  readonly attentions: readonly Attention[];
}

/** マップに出す短い札の文字。 */
export function attentionTag(a: Attention): string {
  switch (a.kind) {
    case 'shortage':
      return `不足 ${a.count}`;
    case 'collectWaiting':
      return `回収待ち ${a.count}`;
    case 'idMismatch':
      return 'ID 食い違い';
    case 'setupError':
      return '段取り替えのエラー';
    case 'needsCheck':
      return `要確認 ${a.count}`;
    case 'replenishWaiting':
      return `補充待ち ${a.count}`;
  }
}

/** カードに出す見出し。 */
export function attentionNote(a: Attention): string {
  switch (a.kind) {
    case 'shortage':
      return `不足が ${a.count}個`;
    case 'collectWaiting':
      return `回収待ちが ${a.count}個`;
    case 'idMismatch':
      return 'ID が食い違った';
    case 'setupError':
      return '段取り替えのエラー';
    case 'needsCheck':
      return `要確認の消耗品が ${a.count}個`;
    case 'replenishWaiting':
      return `補充待ちが ${a.count}個`;
  }
}

/** 名前の札を出す側。外形のうち、受け渡しパネルからはみ出したところが広い側（パネルの文字と重ならないように）。 */
export function labelSide(outline: Rect, panel: Rect): 'top' | 'bottom' {
  const above = Math.max(0, panel.y - outline.y);
  const below = Math.max(0, outline.y + outline.height - (panel.y + panel.height));
  return above > below ? 'top' : 'bottom';
}

/** 札の幅の目安（mm）。全角の文字は1文字ぶん、半角はその半分ほどで数える。 */
export function textWidthMm(text: string, fontSizeMm: number): number {
  let width = 0;
  for (const ch of text) {
    width += (ch.codePointAt(0) ?? 0) < 0x80 ? 0.56 : 1;
  }
  return width * fontSizeMm;
}

/** マップに描くものすべて（パネルと外形）を囲む範囲（mm）。 */
export function contentBounds(map: { readonly widthMm: number; readonly heightMm: number }, equipment: readonly EquipmentOnMap[]): Rect {
  let minX = 0;
  let minY = 0;
  let maxX = map.widthMm;
  let maxY = map.heightMm;
  for (const e of equipment) {
    minX = Math.min(minX, e.outlineMm.x);
    minY = Math.min(minY, e.outlineMm.y);
    maxX = Math.max(maxX, e.outlineMm.x + e.outlineMm.width);
    maxY = Math.max(maxY, e.outlineMm.y + e.outlineMm.height);
  }
  return { x: minX, y: minY, width: maxX - minX, height: maxY - minY };
}
