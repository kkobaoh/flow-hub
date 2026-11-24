import type { EquipmentOnMap } from './equipment';
import type { EquipmentDetail } from './EquipmentCard';

/**
 * カードに出す装置とステーション。位置と外形はサーバーから届いたものを使う。
 * 中身（ストッカーの設定、在庫など）は、サーバーとつなぐまで（CN-7）見本のデータを ID で合わせて使い、見本にないものは空にする。
 * 気をつけることは、まだサーバーから届かないので空。
 */
export function equipmentDetails(server: readonly EquipmentOnMap[], samples: readonly EquipmentDetail[]): EquipmentDetail[] {
  return server.map((e): EquipmentDetail => {
    const sample = samples.find((s) => s.id === e.id && s.kind === e.kind);
    const base = { id: e.id, panel: e.panel, outlineMm: e.outlineMm, attentions: [] };
    if (e.kind === 'station') {
      return { ...base, kind: 'station', details: {}, stock: sample?.kind === 'station' ? sample.stock : [] };
    }
    return sample?.kind === 'device'
      ? { ...base, kind: 'device', details: {}, stocker: sample.stocker, slots: sample.slots }
      : { ...base, kind: 'device', details: {}, stocker: [], slots: { total: 0, free: 0, delivering: 0 } };
  });
}
