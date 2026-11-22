/**
 * マップの見え方（拡大・縮小と、画面の真ん中に来るマップの点）の計算。
 * 画面の大きさと点は px、マップの点は mm で表す。すべて副作用のない計算にし、部品はこれを呼ぶだけにする。
 */

export interface Size {
  readonly width: number;
  readonly height: number;
}

export interface ScreenPoint {
  readonly x: number;
  readonly y: number;
}

/** 見え方。scale は 1mm を何 px で描くか。centerX・centerY は、画面の真ん中に来るマップの点（mm）。 */
export interface View {
  readonly scale: number;
  readonly centerX: number;
  readonly centerY: number;
}

export interface ZoomLimits {
  readonly min: number;
  readonly max: number;
}

export interface ViewBox {
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
}

export interface PinchPoint {
  /** 2本の指の真ん中（画面の左上から px）。 */
  readonly mid: ScreenPoint;
  /** 2本の指の間の距離（px）。 */
  readonly distance: number;
}

/** 全体が収まるように縮めたときに残す余白（px）。 */
const FIT_MARGIN_PX = 24;
/** 拡大したとき、パネル1枚をここまで大きく描く（px）。 */
const MAX_PANEL_PX = 200;

/** 描くものの全体（bounds。mm）が、余白を残して画面に収まる見え方。その真ん中を、画面の真ん中に置く。 */
export function fitView(bounds: ViewBox, screen: Size): View {
  const width = Math.max(screen.width - FIT_MARGIN_PX * 2, 1);
  const height = Math.max(screen.height - FIT_MARGIN_PX * 2, 1);
  return {
    scale: Math.min(width / bounds.width, height / bounds.height),
    centerX: bounds.x + bounds.width / 2,
    centerY: bounds.y + bounds.height / 2,
  };
}

/** 縮小は全体が収まる大きさの半分まで、拡大はパネル1枚が 200px になるまで（全体が収まる大きさより小さくはしない）。 */
export function zoomLimits(fitScale: number, panelSizeMm: number): ZoomLimits {
  return { min: fitScale / 2, max: Math.max(fitScale, MAX_PANEL_PX / panelSizeMm) };
}

function clamp(scale: number, limits: ZoomLimits): number {
  return Math.min(limits.max, Math.max(limits.min, scale));
}

/** 画面に見えている範囲（SVG の viewBox）。 */
export function viewBoxOf(view: View, screen: Size): ViewBox {
  const width = screen.width / view.scale;
  const height = screen.height / view.scale;
  return { x: view.centerX - width / 2, y: view.centerY - height / 2, width, height };
}

/** 画面の点（左上から px）を、マップの点（mm）にする。 */
export function toMap(view: View, screen: Size, point: ScreenPoint): { readonly x: number; readonly y: number } {
  return {
    x: view.centerX + (point.x - screen.width / 2) / view.scale,
    y: view.centerY + (point.y - screen.height / 2) / view.scale,
  };
}

/** マップの点 grabbed が、画面の点 at に来るように、真ん中を決める。 */
function centered(grabbed: { x: number; y: number }, at: ScreenPoint, screen: Size, scale: number): View {
  return {
    scale,
    centerX: grabbed.x - (at.x - screen.width / 2) / scale,
    centerY: grabbed.y - (at.y - screen.height / 2) / scale,
  };
}

/** ドラッグした向き（px）に、マップを動かす。 */
export function panBy(view: View, delta: ScreenPoint): View {
  return { scale: view.scale, centerX: view.centerX - delta.x / view.scale, centerY: view.centerY - delta.y / view.scale };
}

/** 指した点を動かさずに、factor 倍に拡大・縮小する（限りの中で）。 */
export function zoomAt(view: View, screen: Size, point: ScreenPoint, factor: number, limits: ZoomLimits): View {
  return centered(toMap(view, screen, point), point, screen, clamp(view.scale * factor, limits));
}

/** 画面のうち、カードなどに覆われているところ（左からの幅、下からの高さ。px）。 */
export interface Obstruction {
  readonly left: number;
  readonly bottom: number;
}

/** 覆われていない範囲の端から、ここまで近ければ、見えていないものとして動かす（px）。 */
const REVEAL_MARGIN_PX = 48;

/**
 * マップの点 point が、覆われていない範囲に見えていなければ、その範囲の真ん中に来るように動かした見え方を返す。
 * 見えていれば null（動かさない）。拡大の割合は変えない。
 */
export function reveal(view: View, screen: Size, point: { readonly x: number; readonly y: number }, covered: Obstruction): View | null {
  const sx = (point.x - view.centerX) * view.scale + screen.width / 2;
  const sy = (point.y - view.centerY) * view.scale + screen.height / 2;
  const visibleBottom = screen.height - covered.bottom;
  const visible =
    sx - REVEAL_MARGIN_PX >= covered.left &&
    sx + REVEAL_MARGIN_PX <= screen.width &&
    sy - REVEAL_MARGIN_PX >= 0 &&
    sy + REVEAL_MARGIN_PX <= visibleBottom;
  if (visible) {
    return null;
  }
  return centered(point, { x: (covered.left + screen.width) / 2, y: visibleBottom / 2 }, screen, view.scale);
}

/** つまむ操作。指の間を広げた割合だけ拡大し、つまみ始めた点を、今の指の真ん中に付いてこさせる。 */
export function pinchView(start: View, screen: Size, from: PinchPoint, now: PinchPoint, limits: ZoomLimits): View {
  const scale = clamp((start.scale * now.distance) / Math.max(from.distance, 1), limits);
  return centered(toMap(start, screen, from.mid), now.mid, screen, scale);
}
