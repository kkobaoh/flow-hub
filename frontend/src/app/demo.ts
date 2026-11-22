import { agvId } from '../map/agv';
import { heading, panelId } from '../map/model';
import { sampleEquipment } from '../map/sampleLayout';
import type { AgvDetail } from './AgvCard';
import { equipmentId } from '../map/equipment';
import type { EquipmentDetail } from './EquipmentCard';
import type { ConsumableHit } from './ListPanel';
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

/** 見本の消耗品（一覧の消耗品のタブで探す）。 */
export const demoConsumables: readonly ConsumableHit[] = [
  { id: 'T-0101', variety: '[品種A]', condition: '新品', where: 'DEV-1 のストッカー1・スロット1', status: '使える', at: { kind: 'equipment', id: equipmentId('DEV-1') } },
  { id: 'T-0110', variety: '[品種A]', condition: '新品', where: 'ST-1', status: '届けられる', at: { kind: 'equipment', id: equipmentId('ST-1') } },
  { id: 'T-0111', variety: '[品種A]', condition: '新品', where: 'ST-1', status: '届ける予約あり（搬送 #14）', at: { kind: 'equipment', id: equipmentId('ST-1') } },
  { id: 'T-0203', variety: '[品種B]', condition: '中古品', where: 'DEV-1 のストッカー2・スロット1', status: '回収を頼んだ（廃棄）', at: { kind: 'equipment', id: equipmentId('DEV-1') } },
  { id: 'T-0207', variety: '[品種B]', condition: '新品', where: 'AGV-1 のキャリアの中', status: '届ける途中（搬送 #12、DEV-1 へ）', at: { kind: 'agv', id: agvId('AGV-1') } },
  { id: 'X-9001', variety: '不明', condition: '不明', where: 'ST-1', status: '要確認', at: { kind: 'equipment', id: equipmentId('ST-1') } },
];

/** 見本の装置とステーション（マップの外形は map/sampleLayout.ts と同じ）。 */
export const demoEquipment: readonly EquipmentDetail[] = sampleEquipment.map((e): EquipmentDetail => {
  if (e.kind === 'station') {
    return {
      ...e,
      kind: 'station',
      details: {
        needsCheck: 'X-9001 が登録されていない ID として入ってきました。現物を確かめて直してください。',
        replenishWaiting: '[品種A] を上位システムに頼んでいます。',
      },
      stock: [
        { variety: '[品種A]', available: 1, replenishWaiting: 2 },
        { variety: '[品種B]', available: 0, replenishWaiting: 0 },
        { variety: '[品種C]', available: 1, replenishWaiting: 0 },
      ],
    };
  }
  const byDevice: Record<string, Pick<Extract<EquipmentDetail, { kind: 'device' }>, 'details' | 'stocker' | 'slots'>> = {
    'DEV-1': {
      details: {
        collectWaiting: 'T-0203（中古品・廃棄）と T-0410（設定にない品種）の回収を頼んでいます。',
        idMismatch: 'ストッカー2・スロット3 の ID が記録と合いませんでした。回収を頼んでいます。',
      },
      stocker: [
        { variety: '[品種A]', target: 2, have: 2 },
        { variety: '[品種B]', target: 1, have: 1 },
        { variety: '[品種D]', target: 0, have: 1 },
      ],
      slots: { total: 6, free: 1, delivering: 1 },
    },
    'DEV-2': {
      details: { setupError: '[品種C] がストッカーの設定にありません。設定を変えるか、装置の段取りを確かめてください。' },
      stocker: [
        { variety: '[品種A]', target: 2, have: 2 },
        { variety: '[品種B]', target: 2, have: 2 },
      ],
      slots: { total: 4, free: 0, delivering: 0 },
    },
  };
  const extra = byDevice[e.id] ?? { details: {}, stocker: [{ variety: '[品種C]', target: 1, have: 1 }], slots: { total: 2, free: 1, delivering: 0 } };
  return { ...e, kind: 'device', ...extra };
});
