/**
 * 画面で描くためのマップの形。サーバーのマップ（FlowHub.Map）のスナップショットに、
 * 描くための位置（格子の列と行、十字のずれ、パネルの一辺の長さ）を加えたもの。
 */

declare const panelIdBrand: unique symbol;
declare const headingBrand: unique symbol;

/** パネルの ID。空でない文字列。 */
export type PanelId = string & { readonly [panelIdBrand]: true };

/** 絶対角度（度）。北が0度、時計回り。0 以上 360 未満の整数。 */
export type Heading = number & { readonly [headingBrand]: true };

export function panelId(value: string): PanelId {
  if (value.trim() === '') {
    throw new RangeError('パネルの ID が空です。');
  }
  return value as PanelId;
}

export function heading(degrees: number): Heading {
  if (!Number.isInteger(degrees) || degrees < 0 || degrees >= 360) {
    throw new RangeError(`角度は 0 以上 360 未満の整数にしてください（${degrees}）。`);
  }
  return degrees as Heading;
}

export type PanelType = 'Normal' | 'Transfer' | 'Standby' | 'SpecialTransfer';

/** 十字のずれ（mm）。東と南を正にする。 */
export interface OffsetMm {
  readonly x: number;
  readonly y: number;
}

export interface Panel {
  readonly id: PanelId;
  readonly type: PanelType;
  readonly hasChargingPin: boolean;
  /** キャリアを下ろす角度（特殊消耗品受け渡しパネルだけ）。 */
  readonly carrierLoweringHeading?: Heading;
  /** 格子の列（東へ増える）。 */
  readonly column: number;
  /** 格子の行（南へ増える）。 */
  readonly row: number;
  readonly crossOffsetMm: OffsetMm;
}

export interface Connection {
  readonly from: PanelId;
  readonly to: PanelId;
  readonly distanceMm: number;
  /** from から to へ進むときの向き。 */
  readonly heading: Heading;
  readonly oneWay: boolean;
}

export interface MapLayout {
  /** パネルの一辺の長さ（mm）。マップ全体で1つ。 */
  readonly panelSizeMm: number;
  readonly panels: readonly Panel[];
  readonly connections: readonly Connection[];
}
