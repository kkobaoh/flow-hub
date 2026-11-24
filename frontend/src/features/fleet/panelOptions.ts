import type { MapLayout, Panel } from '../routing/model';
import type { DestinationOption } from './ManualPanel';

function labelOf(panel: Panel): string {
  switch (panel.type) {
    case 'Normal':
      return panel.id;
    case 'Transfer':
      return `${panel.id}（受け渡しパネル）`;
    case 'Standby':
      return panel.hasChargingPin ? `${panel.id}（待機パネル・充電ピンあり）` : `${panel.id}（待機パネル）`;
    case 'SpecialTransfer':
      return `${panel.id}（特殊消耗品受け渡しパネル）`;
    default:
      throw new Error(`知らないパネルの種類です: ${JSON.stringify(panel.type satisfies never)}`);
  }
}

/**
 * マニュアルの行き先と、位置リセット・切り離しから戻すときに選べるパネル。マップのパネルを ID の順に並べ、種類がわかる名前にする。
 * 特殊消耗品受け渡しパネルは、着いたときの向きをキャリアを下ろす角度に決めておく。
 */
export function panelOptions(layout: MapLayout): DestinationOption[] {
  return [...layout.panels]
    .sort((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0))
    .map((panel) =>
      panel.type === 'SpecialTransfer' && panel.carrierLoweringHeading !== undefined
        ? { id: panel.id, label: labelOf(panel), fixedArrival: panel.carrierLoweringHeading }
        : { id: panel.id, label: labelOf(panel) },
    );
}
