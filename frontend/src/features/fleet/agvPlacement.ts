import type { AgvOnMap, UnplacedAgv } from './agv';
import type { AgvDetail } from './AgvCard';

/** マップの上（描いているマップのパネル）にいる AGV を、マップに描く印にする。 */
export function placedAgvs(agvs: readonly AgvDetail[]): AgvOnMap[] {
  return agvs.flatMap((a) =>
    a.position.kind === 'onMap' ? [{ id: a.id, label: a.label, panel: a.position.panel, heading: a.position.heading, status: a.status }] : [],
  );
}

/** 位置がわからない AGV と、描いているマップにないパネルにいる AGV を、マップの下の列に描く印にする。 */
export function unplacedAgvs(agvs: readonly AgvDetail[]): UnplacedAgv[] {
  return agvs.flatMap((a) => (a.position.kind === 'onMap' ? [] : [{ id: a.id, label: a.label, status: a.status }]));
}
