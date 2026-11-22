import { agvId } from '../map/agv';
import { heading, panelId } from '../map/model';
import type { AgvDetail } from './AgvCard';
import type { DestinationOption } from './ManualPanel';
import type { Message } from './MessageRail';

/**
 * 見本のデータ（サーバーとつながるまで、画面を動かして見るため）。
 * デザインの試作と同じ並び（map/sampleLayout.ts）の上に置く。
 */
export const demoAgvs: readonly AgvDetail[] = [
  {
    id: agvId('AGV-1'), label: '1', panel: panelId('P3'), heading: heading(90),
    status: { activity: 'moving', charging: false, carrierMoving: false },
    control: 'auto', operation: 'traveling', transport: '#12 ST-1 から DEV-1 へ', batteryPercent: 72,
  },
  {
    id: agvId('AGV-2'), label: '2', panel: panelId('SB2'), heading: heading(90),
    status: { activity: 'idle', charging: true, carrierMoving: false },
    control: 'assignmentPaused', operation: 'idle', transport: null, batteryPercent: 35,
  },
  {
    id: agvId('AGV-3'), label: '3', panel: panelId('SB'), heading: heading(90),
    status: { activity: 'idle', charging: false, carrierMoving: false },
    control: 'auto', operation: 'idle', transport: null, batteryPercent: 88,
  },
];

export const demoRoutes = [[panelId('P3'), panelId('P4'), panelId('P5'), panelId('P6'), panelId('D1')]];
export const demoReserved = [panelId('P3'), panelId('P4')];

export const demoMessages: readonly Message[] = [
  { id: 'm6', time: '10:42', title: '予約が取れずに止まっている', detail: 'AGV-1 が、パネル P4 へ向かう予約を待っています。', unread: true },
  { id: 'm5', time: '10:31', title: '行ける充電ポートがない', detail: 'AGV-2 は、充電ポートが空いたら向かいます。', unread: true },
  { id: 'm4', time: '10:05', title: '受け渡しの ID が食い違った', detail: 'DEV-1 で、搬送 #11 の受け渡しの ID が合いませんでした。', unread: true },
  { id: 'm3', time: '09:12', title: '補充を頼んだ', detail: '[品種A] を 2個、上位システムに頼みました。', unread: false },
];

export const demoDestinations: readonly DestinationOption[] = [
  { id: panelId('SB'), label: 'SB（待機パネル）' },
  { id: panelId('SB2'), label: 'SB2（待機パネル・充電ピンあり）' },
  { id: panelId('ST'), label: 'ST（ST-1 の受け渡しパネル）' },
  { id: panelId('D1'), label: 'D1（DEV-1 の受け渡しパネル）' },
  { id: panelId('P1'), label: 'P1' },
];
