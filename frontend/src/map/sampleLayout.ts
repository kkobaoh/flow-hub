import { heading, panelId, type Connection, type MapLayout, type Panel, type PanelType } from './model';

/** デザインの試作と同じ並びの、見本のマップ（Storybook とテストで使う）。 */
function p(id: string, column: number, row: number, type: PanelType = 'Normal', extra: Partial<Panel> = {}): Panel {
  return { id: panelId(id), type, hasChargingPin: false, column, row, crossOffsetMm: { x: 0, y: 0 }, ...extra };
}

function c(from: string, to: string, deg: number): Connection {
  return { from: panelId(from), to: panelId(to), distanceMm: 1000, heading: heading(deg), oneWay: false };
}

export const sampleLayout: MapLayout = {
  panelSizeMm: 1000,
  panels: [
    p('ST', 2, 0, 'Transfer'),
    p('SB', 0, 1, 'Standby'),
    p('P1', 1, 1), p('P2', 2, 1), p('P3', 3, 1), p('P4', 4, 1),
    p('SB2', 5, 1, 'Standby', { hasChargingPin: true }),
    p('P8', 1, 2), p('P7', 2, 2), p('P6', 3, 2), p('P5', 4, 2),
    p('D2', 2, 3, 'Transfer'), p('D1', 3, 3, 'Transfer'),
    p('SP', 4, 3, 'SpecialTransfer', { carrierLoweringHeading: heading(45) }),
  ],
  connections: [
    c('SB', 'P1', 90), c('P1', 'P2', 90), c('P2', 'P3', 90), c('P3', 'P4', 90), c('P4', 'SB2', 90),
    c('P8', 'P7', 90), c('P7', 'P6', 90), c('P6', 'P5', 90), c('P1', 'P8', 180), c('P4', 'P5', 180),
    c('P2', 'ST', 0), c('P7', 'D2', 180), c('P6', 'D1', 180), c('P5', 'SP', 180),
  ],
};
