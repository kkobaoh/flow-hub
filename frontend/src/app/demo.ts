import { agvId } from '../map/agv';
import { heading, panelId } from '../map/model';
import { sampleEquipment } from '../map/sampleLayout';
import type { AgvDetail } from './AgvCard';
import { equipmentId } from '../map/equipment';
import type { EquipmentDetail } from './EquipmentCard';
import type { ConsumableHit } from './ListPanel';
import type { DestinationOption } from './ManualPanel';
import type { Message } from './MessageRail';
import type { StationInventory } from './station';
import type { Transport } from './transports';
import type { DeviceInside, HeldConsumable, SavedRow, SettingRow } from './devices';

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

const today = (hhmm: string) => {
  const [h, m] = hhmm.split(':').map(Number);
  const d = new Date();
  d.setHours(h ?? 0, m ?? 0, 0, 0);
  return d;
};

export const demoTransports: readonly Transport[] = [
  {
    number: 14,
    status: { stage: 'queued' },
    kind: '届ける',
    from: 'ST-1',
    to: 'DEV-2',
    cargo: '[品種A] 2個',
    neededBy: today('10:20'),
    requestedAt: today('09:55'),
    agv: null,
    step: '割り当て待ち',
    stepNote: '割り当てられる AGV がいません',
  },
  {
    number: 15,
    status: { stage: 'queued' },
    kind: '回収する',
    from: 'DEV-1',
    to: 'ST-1',
    cargo: '[品種B] 1個（設定にない品種）',
    neededBy: today('10:35'),
    requestedAt: today('10:01'),
    agv: null,
    step: '割り当て待ち',
    stepNote: '',
  },
  {
    number: 12,
    status: { stage: 'running' },
    kind: '届ける',
    from: 'ST-1',
    to: 'DEV-1',
    cargo: '[品種A] 1個、[品種B] 1個',
    neededBy: today('10:02'),
    requestedAt: today('09:30'),
    agv: agvId('AGV-1'),
    step: '搬送先へ移動',
    stepNote: '積み込み済み',
  },
  {
    number: 11,
    status: { stage: 'suspended', positionResetDone: false },
    kind: '回収する',
    from: 'DEV-1',
    to: 'ST-1',
    cargo: '不明な消耗品 1個（ID の食い違い）',
    neededBy: today('09:40'),
    requestedAt: today('09:05'),
    agv: agvId('AGV-3'),
    step: '中断',
    stepNote: 'AGV の異常で止まりました',
  },
];

const held = (id: string | null, variety: string, note: string, tone: HeldConsumable['tone'] = 'normal'): HeldConsumable => ({ id, variety, note, tone });

/** 見本の装置の中（装置とストッカーの画面）。数はマップのカード（demoEquipment）とそろえている。 */
export const demoDevices: readonly DeviceInside[] = [
  {
    id: equipmentId('DEV-1'),
    panel: panelId('D1'),
    processing: held('T-0100', '[品種A]', '中古品・取り込み中（元はストッカー1 のスロット2）'),
    stockers: [
      {
        name: 'ストッカー1',
        layout: 'slots',
        slots: [
          { number: 1, content: { kind: 'held', consumable: held('T-0101', '[品種A]', '新品') } },
          { number: 2, content: { kind: 'reserved', consumable: held('T-0100', '[品種A]', '加工部に取り込み中') } },
          { number: 3, content: { kind: 'reserved', consumable: held('T-0207', '[品種B]', '届ける途中（搬送 #12）') } },
        ],
      },
      {
        name: 'ストッカー2',
        layout: 'slots',
        slots: [
          { number: 1, content: { kind: 'held', consumable: held('T-0203', '[品種B]', '中古品・回収を頼んだ（廃棄）', 'collect') } },
          { number: 2, content: { kind: 'held', consumable: held('T-0410', '[品種D]', '新品・回収を頼んだ（設定にない）', 'collect') } },
          { number: 3, content: { kind: 'held', consumable: held(null, '不明な消耗品', 'ID が食い違った。回収を頼んだ', 'blocked') } },
        ],
      },
    ],
    handover: null,
    setting: [
      { variety: '[品種A]', target: 2, have: 2, status: 'enough' },
      { variety: '[品種B]', target: 1, have: 1, status: 'enough' },
      { variety: '[品種D]', target: 0, have: 1, status: 'notInSetting' },
    ],
  },
  {
    id: equipmentId('DEV-2'),
    panel: panelId('D2'),
    processing: null,
    stockers: [
      {
        name: 'ストッカー1',
        layout: 'slots',
        slots: [
          { number: 1, content: { kind: 'held', consumable: held('T-0102', '[品種A]', '新品') } },
          { number: 2, content: { kind: 'held', consumable: held('T-0103', '[品種A]', '新品') } },
          { number: 3, content: { kind: 'held', consumable: held('T-0204', '[品種B]', '新品') } },
          { number: 4, content: { kind: 'held', consumable: held('T-0205', '[品種B]', '新品') } },
        ],
      },
    ],
    handover: null,
    setting: [
      { variety: '[品種A]', target: 2, have: 2, status: 'enough' },
      { variety: '[品種B]', target: 2, have: 2, status: 'enough' },
    ],
  },
  {
    id: equipmentId('DEV-3'),
    panel: panelId('SP'),
    processing: null,
    stockers: [{ name: 'ストッカー1', layout: 'plate', items: [held('T-0301', '[品種C]', '新品')] }],
    handover: held('T-0302', '[品種C]', '受け渡し待ち'),
    setting: [{ variety: '[品種C]', target: 1, have: 1, status: 'enough' }],
  },
];

/** 見本の品種。 */
export const demoVarieties: readonly string[] = ['[品種A]', '[品種B]', '[品種C]', '[品種D]'];

/**
 * 見本の画面で、保存したストッカーの設定を装置に当てる（本物は、サーバーが判断して知らせる）。
 * 設定から外した品種で、まだ装置にあるものは「設定にない」にする。
 */
export function applyDemoSetting(device: DeviceInside, saved: readonly SavedRow[]): DeviceInside {
  const haveOf = (variety: string) => device.setting.find((r) => r.variety === variety)?.have ?? 0;
  const inSetting: SettingRow[] = saved.map((r) => {
    const have = haveOf(r.variety);
    return { variety: r.variety, target: r.target, have, status: have < r.target ? 'short' : have > r.target ? 'excess' : 'enough' };
  });
  const leftOver: SettingRow[] = device.setting
    .filter((r) => r.have > 0 && !saved.some((s) => s.variety === r.variety))
    .map((r) => ({ variety: r.variety, target: 0, have: r.have, status: 'notInSetting' }));
  return { ...device, setting: [...inSetting, ...leftOver] };
}

const onDay = (day: number, hhmm: string) => {
  const d = today(hhmm);
  d.setDate(d.getDate() - day);
  return d;
};

/** 見本のステーションの在庫（数はマップのカード（demoEquipment）とそろえている）。 */
export const demoStations: readonly StationInventory[] = [
  {
    id: equipmentId('ST-1'),
    panel: panelId('ST'),
    summary: [
      { variety: '[品種A]', available: 1, reserved: 1, replenishWaiting: 2 },
      { variety: '[品種B]', available: 0, reserved: 0, replenishWaiting: 0 },
      { variety: '[品種C]', available: 1, reserved: 0, replenishWaiting: 0 },
    ],
    items: [
      { id: 'T-0110', variety: '[品種A]', condition: '新品', receivedAt: onDay(2, '14:02'), state: { kind: 'available' } },
      { id: 'T-0111', variety: '[品種A]', condition: '新品', receivedAt: onDay(2, '14:02'), state: { kind: 'reserved', transport: 14, assigned: false } },
      { id: 'T-0208', variety: '[品種B]', condition: '新品', receivedAt: onDay(3, '09:30'), state: { kind: 'nominated', device: 'DEV-1' } },
      { id: 'T-0303', variety: '[品種C]', condition: '新品', receivedAt: onDay(4, '16:45'), state: { kind: 'available' } },
      {
        id: 'X-9001',
        variety: null,
        condition: null,
        receivedAt: onDay(0, '08:12'),
        state: { kind: 'needsCheck', reason: '登録されていない ID として入ってきました。' },
      },
    ],
  },
];
