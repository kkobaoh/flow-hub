import type { Heading, PanelId } from './model';

declare const agvIdBrand: unique symbol;

/** AGV の ID。空でない文字列。 */
export type AgvId = string & { readonly [agvIdBrand]: true };

export function agvId(value: string): AgvId {
  if (value.trim() === '') {
    throw new RangeError('AGV の ID が空です。');
  }
  return value as AgvId;
}

/** AGV が走っているか（走行動作中）、止まっているか。 */
export type Activity = 'moving' | 'idle';

/**
 * 充電しているか。AGV からは知らせがないので、今は、いつもわからない（docs/screens.md「画面とサーバーの通信」）。
 * わからないときは、充電ピンのあるパネルでも、充電中（緑）とも充電できていない（紫）とも出さない。
 */
export type Charging = 'charging' | 'notCharging' | 'unknown';

export interface AgvStatus {
  readonly activity: Activity;
  readonly charging: Charging;
  /** キャリアを上げ下げしているか。 */
  readonly carrierMoving: boolean;
}

/** マップの上に描く AGV。 */
export interface AgvOnMap {
  readonly id: AgvId;
  /** 印に書く短い名前（番号）。 */
  readonly label: string;
  readonly panel: PanelId;
  readonly heading: Heading;
  readonly status: AgvStatus;
}

/** マップの下の列に描く、位置がわからない AGV（マップにないパネルにいるものも含む）。向きは描かない。 */
export interface UnplacedAgv {
  readonly id: AgvId;
  /** 印に書く短い名前。 */
  readonly label: string;
  readonly status: AgvStatus;
}

/**
 * AGV のいる位置。
 * - onMap：描いているマップの上のパネルにいる。
 * - unknown：位置がわからない（位置リセットの前など）。
 * - offMap：サーバーからはパネルが届いたが、描いているマップにそのパネルがない。
 */
export type AgvPosition =
  | { readonly kind: 'onMap'; readonly panel: PanelId; readonly heading: Heading }
  | { readonly kind: 'unknown' }
  | { readonly kind: 'offMap'; readonly panel: PanelId; readonly heading: Heading };

/** 標示灯の色。青：走行動作中、黄：待機中、緑：充電中、紫：充電できるはずなのにできていない。 */
export type LightColor = 'moving' | 'waiting' | 'charging' | 'chargeFailed';

export interface IndicatorLight {
  readonly color: LightColor;
  readonly blinking: boolean;
}

/**
 * 標示灯を、AGV の状態といるパネルから決める。
 * - 走行動作中は、青の点滅。
 * - 充電ピンのないパネルで待機しているときは、黄。
 * - 充電ピンのあるパネルでは、充電していれば緑、していなければ紫。キャリアを上げ下げしているあいだは点滅する。
 * - 充電しているかがわからなければ、充電ピンのあるパネルでも黄。
 */
export function indicatorLight(status: AgvStatus, onChargingPin: boolean): IndicatorLight {
  if (status.activity === 'moving') {
    return { color: 'moving', blinking: true };
  }
  if (!onChargingPin || status.charging === 'unknown') {
    return { color: 'waiting', blinking: false };
  }
  return { color: status.charging === 'charging' ? 'charging' : 'chargeFailed', blinking: status.carrierMoving };
}

/** 標示灯の状態を、読み上げる言葉にする。 */
export function lightLabel(color: LightColor): string {
  switch (color) {
    case 'moving':
      return '走行動作中';
    case 'waiting':
      return '待機中';
    case 'charging':
      return '充電中';
    case 'chargeFailed':
      return '充電できていない';
  }
}
