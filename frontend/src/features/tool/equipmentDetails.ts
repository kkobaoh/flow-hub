import type { DeviceStatus, EquipmentStatusSnapshot, StationStatus } from './api/equipmentStatus';
import type { Attention, AttentionKind, EquipmentOnMap } from './equipment';
import type { EquipmentDetail } from './EquipmentCard';

type Details = Partial<Record<AttentionKind, string>>;

/** 数が 1 以上のものだけを、気をつけることにする。 */
function attentionsOf(counts: readonly (readonly [AttentionKind, number])[]): Attention[] {
  return counts.filter(([, n]) => n > 0).map(([kind, count]) => ({ kind, count }));
}

function deviceDetails(s: DeviceStatus): Details {
  const details: Details = {};
  if (s.shortage > 0) {
    details.shortage = `届ける途中のものを引いて、まだ ${s.shortage}個足りません。`;
  }
  if (s.collectWaiting > 0) {
    details.collectWaiting = `${s.collectWaiting}個の回収を待っています。`;
  }
  if (s.mismatchedSlots.length > 0) {
    const slots = s.mismatchedSlots.map((p) => `ストッカー${p.stocker}・スロット${p.slot}`).join('、');
    details.idMismatch = `${slots} の ID が記録と合いませんでした。AGV で回収して、ステーションで現物を確かめます。`;
  }
  if (s.setupErrors.length > 0) {
    details.setupError = `${s.setupErrors.join('、')} がストッカーの設定にありません。設定を変えるか、装置の段取りを確かめてください。`;
  }
  return details;
}

function stationDetails(s: StationStatus): Details {
  const details: Details = {};
  if (s.needsCheck > 0) {
    details.needsCheck = `要確認の消耗品が ${s.needsCheck}個あります。現物を確かめて直してください。`;
  }
  if (s.replenishWaiting > 0) {
    details.replenishWaiting = `補充を頼んで、まだ ${s.replenishWaiting}個入っていません。`;
  }
  return details;
}

/**
 * マップに描き、カードに出す装置とステーション。位置と外形はマップの様子から、気をつけることと中身（ストッカー、在庫）は装置とステーションの様子から取る。
 * 様子がまだ届いていないものは、中身を空にする。
 */
export function equipmentDetails(onMap: readonly EquipmentOnMap[], statuses: EquipmentStatusSnapshot | null): EquipmentDetail[] {
  return onMap.map((e): EquipmentDetail => {
    const base = { id: e.id, panel: e.panel, outlineMm: e.outlineMm };
    if (e.kind === 'station') {
      const s = statuses?.stations.find((x) => x.id === e.id);
      return s
        ? {
            ...base,
            kind: 'station',
            attentions: attentionsOf([
              ['needsCheck', s.needsCheck],
              ['replenishWaiting', s.replenishWaiting],
            ]),
            details: stationDetails(s),
            stock: s.stock,
          }
        : { ...base, kind: 'station', attentions: [], details: {}, stock: [] };
    }
    const s = statuses?.devices.find((x) => x.id === e.id);
    return s
      ? {
          ...base,
          kind: 'device',
          attentions: attentionsOf([
            ['shortage', s.shortage],
            ['collectWaiting', s.collectWaiting],
            ['idMismatch', s.mismatchedSlots.length],
            ['setupError', s.setupErrors.length],
          ]),
          details: deviceDetails(s),
          stocker: s.stocker,
          slots: s.slots,
        }
      : { ...base, kind: 'device', attentions: [], details: {}, stocker: [], slots: { total: 0, free: 0, delivering: 0 } };
  });
}
