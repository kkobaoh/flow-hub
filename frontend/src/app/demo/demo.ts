import { agvId } from '../../features/fleet/agv';
import { heading, panelId } from '../../features/routing/model';
import { sampleEquipment, sampleLayout } from './sampleLayout';
import type { AgvDetail } from '../../features/fleet/AgvCard';
import { equipmentId } from '../../features/tool/equipment';
import type { EquipmentDetail } from '../../features/tool/EquipmentCard';
import type { ConsumableHit } from '../../features/routing/ListPanel';
import type { DestinationOption } from '../../features/fleet/ManualPanel';
import type { Message } from '../../features/alarm/MessageRail';
import { initialRoles } from '../../features/auth/permissions';
import type { Role, UserAccount } from '../../features/user-management/users';
import type { StationInventory } from '../../features/tool/station';
import type { Transport } from '../../features/dispatch/transports';
import type { MapDraft } from '../../features/routing/mapEdit';
import type { CarrierHeight } from '../../pages/MapEditPage';
import type { AgvEntry, AgvType } from '../../features/fleet/agvs';
import type { DeviceConfig, ProcessKind, StationConfig, Variety } from '../../features/tool/config';
import type { ConsumableLocation, ConsumableRecord } from '../../features/tool/consumables';
import type { DeviceInside, HeldConsumable, SavedRow, SettingRow } from '../../features/tool/devices';

/**
 * 見本のデータ（サーバーとつながるまで、画面を動かして見るため）。
 * デザインの試作と同じ並び（map/sampleLayout.ts）の上に置く。
 */
export const demoAgvs: readonly AgvDetail[] = [
  {
    id: agvId('AGV-1'), label: '1', position: { kind: 'onMap', panel: panelId('P3'), heading: heading(90) },
    status: { activity: 'moving', charging: 'notCharging', carrierMoving: false },
    control: 'auto', operation: 'traveling', transport: { label: '#12 ST-1 から DEV-1 へ', step: '搬送先へ移動' }, batteryPercent: 72,
  },
  {
    id: agvId('AGV-2'), label: '2', position: { kind: 'onMap', panel: panelId('SB2'), heading: heading(90) },
    status: { activity: 'idle', charging: 'charging', carrierMoving: false },
    control: 'assignmentPaused', operation: 'idle', transport: null, batteryPercent: 35,
  },
  {
    id: agvId('AGV-3'), label: '3', position: { kind: 'onMap', panel: panelId('SB'), heading: heading(90) },
    status: { activity: 'idle', charging: 'notCharging', carrierMoving: false },
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

const consumable = (
  id: string,
  variety: string | null,
  location: ConsumableLocation,
  more: Partial<ConsumableRecord> = {},
): ConsumableRecord => ({
  id,
  variety,
  processKind: variety === null ? null : variety === '[品種C]' ? '[加工の種類B]' : '[加工の種類A]',
  condition: variety === null ? null : '新品',
  location,
  needsCheck: false,
  reservation: null,
  nomination: null,
  shipping: false,
  history: [],
  ...more,
});

/** 見本の消耗品の記録（消耗品の検索と手入力の画面）。所在は、装置とステーションの見本とそろえている。 */
export const demoRecords: readonly ConsumableRecord[] = [
  consumable(
    'T-0101',
    '[品種A]',
    { kind: 'slot', device: 'DEV-1', stocker: 1, slot: 1 },
    {
      history: [
        { at: onDay(1, '09:58'), what: 'DEV-1 のストッカー1・スロット1 に入れた', detail: '搬送 #10 で届けました' },
        { at: onDay(1, '09:51'), what: 'AGV-1 に積み込んだ', detail: 'ST-1 から、搬送 #10' },
        { at: onDay(1, '09:40'), what: '届ける予約が付いた', detail: 'DEV-1 の [品種A] の不足を補うため' },
        { at: onDay(2, '14:02'), what: 'ST-1 に入庫した', detail: '新品として登録しました' },
      ],
    },
  ),
  consumable('T-0100', '[品種A]', { kind: 'processing', device: 'DEV-1' }, { condition: '中古品' }),
  consumable('T-0110', '[品種A]', { kind: 'station', station: 'ST-1' }, { history: [{ at: onDay(2, '14:02'), what: 'ST-1 に入庫した', detail: '新品として登録しました' }] }),
  consumable('T-0111', '[品種A]', { kind: 'station', station: 'ST-1' }, { reservation: { transport: 14, assigned: false, purpose: 'DEV-2 へ届ける' } }),
  consumable('T-0207', '[品種B]', { kind: 'agv', agv: 'AGV-1' }, { reservation: { transport: 12, assigned: true, purpose: 'DEV-1 へ届ける' } }),
  consumable('T-0203', '[品種B]', { kind: 'slot', device: 'DEV-1', stocker: 2, slot: 1 }, { condition: '中古品' }),
  consumable('T-0208', '[品種B]', { kind: 'station', station: 'ST-1' }, { nomination: { device: 'DEV-1', assigned: false } }),
  consumable('T-0303', '[品種C]', { kind: 'station', station: 'ST-1' }),
  consumable('T-0410', '[品種D]', { kind: 'slot', device: 'DEV-1', stocker: 2, slot: 2 }),
  consumable('X-9001', null, { kind: 'station', station: 'ST-1' }, { needsCheck: true }),
];

/** 見本の AGV の登録（AGV-4 は、走行の側にだけ登録されている食い違い）。 */
export const demoAgvEntries: readonly AgvEntry[] = [
  ...demoAgvs.map((a): AgvEntry => ({ id: a.id, type: a.id === 'AGV-3' ? '[種類B]' : '[種類A]', carrier: '[キャリアの種類A]', control: a.control, registration: 'both' })),
  { id: agvId('AGV-4'), type: '[種類A]', carrier: null, control: null, registration: 'trafficOnly' },
];

/** 見本の AGV の種類。 */
export const demoAgvTypes: readonly AgvType[] = [
  { name: '[種類A]', forward: 1000, backward: 500, rotation: 45, chargeAt: 20, resumeAt: 80, special: false },
  { name: '[種類B]', forward: 800, backward: 400, rotation: 30, chargeAt: 20, resumeAt: 80, special: true },
];

/** 見本のキャリアの種類。 */
export const demoCarriers: readonly string[] = ['[キャリアの種類A]', '[キャリアの種類B]'];

/** 見本の加工の種類。 */
export const demoProcessKinds: readonly ProcessKind[] = [
  { name: '[加工の種類A]', method: 'dedicated', carrier: '[キャリアの種類A]', capacity: 3 },
  { name: '[加工の種類B]', method: 'plate', carrier: '[キャリアの種類B]', capacity: 4 },
];

/** 見本の品種と ID の対応（在庫と記録は demoRecords とそろえている）。 */
export const demoConfigVarieties: readonly Variety[] = [
  { name: '[品種A]', processKind: '[加工の種類A]', ids: ['T-0100', 'T-0101', 'T-0102', 'T-0103', 'T-0110', 'T-0111'], stockedIds: ['T-0100', 'T-0101', 'T-0110', 'T-0111'], inUse: true },
  { name: '[品種B]', processKind: '[加工の種類A]', ids: ['T-0203', 'T-0204', 'T-0205', 'T-0207', 'T-0208'], stockedIds: ['T-0203', 'T-0207', 'T-0208'], inUse: true },
  { name: '[品種C]', processKind: '[加工の種類B]', ids: ['T-0301', 'T-0302', 'T-0303'], stockedIds: ['T-0303'], inUse: true },
  { name: '[品種D]', processKind: '[加工の種類A]', ids: ['T-0410'], stockedIds: ['T-0410'], inUse: true },
  { name: '[品種E]', processKind: '[加工の種類A]', ids: [], stockedIds: [], inUse: false },
];

/** 見本の装置の構成（中身は demoDevices とそろえている）。 */
export const demoDeviceConfigs: readonly DeviceConfig[] = [
  { id: 'DEV-1', panel: 'D1', layout: { kind: 'slots', slots: [3, 3] }, occupied: [[1, 2, 3], [1, 2, 3]], busy: true },
  { id: 'DEV-2', panel: 'D2', layout: { kind: 'slots', slots: [4] }, occupied: [[1, 2, 3, 4]], busy: true },
  { id: 'DEV-3', panel: 'SP', layout: { kind: 'plate', stockers: 1 }, occupied: [[1]], busy: true },
];

/** 見本のステーションの構成。 */
export const demoStationConfigs: readonly StationConfig[] = [{ id: 'ST-1', panel: 'ST', busy: true }];

/** 見本のロール（最初の4つと、加えたロール）。 */
export const demoRoles: readonly Role[] = [
  { id: 'viewer', name: '閲覧のみ', permissions: initialRoles.閲覧のみ, timeout: { amount: 7, unit: 'days' }, isDefault: true, fixed: false },
  { id: 'operator', name: '運用', permissions: initialRoles.運用, timeout: { amount: 8, unit: 'hours' }, isDefault: false, fixed: false },
  { id: 'maintainer', name: '保守', permissions: initialRoles.保守, timeout: { amount: 8, unit: 'hours' }, isDefault: false, fixed: false },
  { id: 'admin', name: '管理者', permissions: initialRoles.管理者, timeout: { amount: 8, unit: 'hours' }, isDefault: false, fixed: true },
];

/** 見本のユーザー。 */
export const demoUsers: readonly UserAccount[] = [
  { id: 'admin01', role: 'admin', lastLogin: onDay(0, '08:02'), state: { kind: 'active' } },
  { id: 'ops01', role: 'operator', lastLogin: onDay(0, '07:55'), state: { kind: 'active' } },
  { id: 'maint01', role: 'maintainer', lastLogin: onDay(1, '17:30'), state: { kind: 'locked', until: today('10:15') } },
  { id: 'line-display', role: 'viewer', lastLogin: onDay(7, '06:00'), state: { kind: 'active' } },
  { id: 'ops02', role: 'viewer', lastLogin: null, state: { kind: 'mustChangePassword' } },
];

/** 見本の下書き（公開中の版の写し。外形は map/sampleLayout.ts の装置とステーション）。 */
export const demoMapDraft: MapDraft = {
  layout: sampleLayout,
  outlines: Object.fromEntries(sampleEquipment.map((e) => [e.id, e.outlineMm])),
};

/** 見本のキャリアの高さ（公開中の版の受け渡しパネル）。 */
export const demoHeights: Readonly<Record<string, CarrierHeight | null>> = {
  D1: { heightMm: 320, source: 'measured', at: onDay(1, '09:00') },
  D2: { heightMm: 300, source: 'manual', at: onDay(3, '15:20') },
  SP: null,
  ST: { heightMm: 280, source: 'manual', at: onDay(5, '11:05') },
};

/**
 * 見本の画面で、下書きを確かめる（本物は、サーバーのマップのコンテキストが検証する）。
 * 見本では、特殊消耗品受け渡しパネルの下ろす角度と、行き止まりのパネルのつながりの数だけ確かめる。
 */
export function demoValidate(draft: MapDraft): readonly string[] {
  const problems: string[] = [];
  for (const p of draft.layout.panels) {
    if (p.type === 'SpecialTransfer' && p.carrierLoweringHeading === undefined) {
      problems.push(`${p.id} に、キャリアを下ろす角度が入っていません。`);
    }
    const links = draft.layout.connections.filter((c) => c.from === p.id || c.to === p.id).length;
    if ((p.type === 'Transfer' || p.type === 'Standby') && links !== 1) {
      problems.push(`${p.id} は行き止まりのパネルなので、つながりを1本だけにしてください（いまは ${links}本）。`);
    }
  }
  return problems;
}
