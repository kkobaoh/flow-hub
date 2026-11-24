import { equipmentId, type EquipmentOnMap } from '../../tool/equipment';
import { heading, panelId, type Connection, type MapLayout, type Panel, type PanelType } from '../model';
import { parseVersion, type Versioned } from '../../../shared/realtime/versioned';

/** サーバーから届くマップの様子（公開中の版と、その上に描く装置とステーション）。版が変わるか、装置とステーションが変わるたびに番号が進む。 */
export interface MapSnapshot extends Versioned {
  /** 公開中のマップの版の番号。まだ公開していなければ 0（パネルは空）。 */
  readonly mapVersion: number;
  readonly layout: MapLayout;
  /** マップに描く装置とステーション（気をつけることは、まだサーバーから届かないので空）。 */
  readonly equipment: readonly EquipmentOnMap[];
}

const PANEL_TYPES: ReadonlySet<string> = new Set<PanelType>(['Normal', 'Transfer', 'Standby', 'SpecialTransfer']);

const isRecord = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null;

function int(value: unknown, what: string): number {
  if (typeof value !== 'number' || !Number.isInteger(value)) {
    throw new TypeError(`${what}が整数ではありません（${String(value)}）。`);
  }
  return value;
}

function text(value: unknown, what: string): string {
  if (typeof value !== 'string' || value.trim() === '') {
    throw new TypeError(`${what}がありません。`);
  }
  return value;
}

function list(value: unknown, what: string): readonly unknown[] {
  if (!Array.isArray(value)) {
    throw new TypeError(`${what}の並びがありません。`);
  }
  return value;
}

function parsePanel(value: unknown): Panel {
  if (!isRecord(value)) {
    throw new TypeError('パネルの形が正しくありません。');
  }
  const id = text(value.id, 'パネルの ID');
  const type = value.type;
  if (typeof type !== 'string' || !PANEL_TYPES.has(type)) {
    throw new TypeError(`知らない種類のパネルです（${String(type)}）。`);
  }
  if (typeof value.hasChargingPin !== 'boolean') {
    throw new TypeError(`パネル ${id} の充電ピンの有無がありません。`);
  }
  const panel: Panel = {
    id: panelId(id),
    type: type as PanelType,
    hasChargingPin: value.hasChargingPin,
    column: int(value.column, `パネル ${id} の列`),
    row: int(value.row, `パネル ${id} の行`),
    crossOffsetMm: { x: int(value.crossOffsetEastMm, `パネル ${id} の十字のずれ`), y: int(value.crossOffsetSouthMm, `パネル ${id} の十字のずれ`) },
  };
  return value.carrierLoweringHeading === null || value.carrierLoweringHeading === undefined
    ? panel
    : { ...panel, carrierLoweringHeading: heading(int(value.carrierLoweringHeading, `パネル ${id} の下ろす角度`)) };
}

function parseConnection(value: unknown): Connection {
  if (!isRecord(value) || typeof value.oneWay !== 'boolean') {
    throw new TypeError('つながりの形が正しくありません。');
  }
  return {
    from: panelId(text(value.from, 'つながりの端')),
    to: panelId(text(value.to, 'つながりの端')),
    distanceMm: int(value.distanceMm, 'つながりの距離'),
    heading: heading(int(value.heading, 'つながりの向き')),
    oneWay: value.oneWay,
  };
}

function parseEquipment(value: unknown): EquipmentOnMap {
  if (!isRecord(value) || !isRecord(value.outline)) {
    throw new TypeError('装置かステーションの形が正しくありません。');
  }
  const kind = value.kind;
  if (kind !== 'device' && kind !== 'station') {
    throw new TypeError(`知らない装置の種類です（${String(kind)}）。`);
  }
  const id = text(value.id, '装置かステーションの ID');
  const width = int(value.outline.widthMm, `${id} の外形の幅`);
  const height = int(value.outline.heightMm, `${id} の外形の奥行き`);
  if (width < 1 || height < 1) {
    throw new TypeError(`${id} の外形の幅と奥行きは 1 以上にしてください。`);
  }
  return {
    id: equipmentId(id),
    kind,
    panel: panelId(text(value.panel, `${id} の受け渡しパネル`)),
    outlineMm: { x: int(value.outline.xMm, `${id} の外形の位置`), y: int(value.outline.yMm, `${id} の外形の位置`), width, height },
    attentions: [],
  };
}

/** サーバーから届いた値を検証して、マップの様子にする。形が正しくなければ例外にする。 */
export function parseMap(value: unknown): MapSnapshot {
  if (!isRecord(value)) {
    throw new TypeError('マップの様子の形が正しくありません。');
  }
  const mapVersion = int(value.mapVersion, 'マップの版の番号');
  const panelSizeMm = int(value.panelSizeMm, 'パネルの一辺の長さ');
  const panels = list(value.panels, 'パネル').map(parsePanel);
  if (panels.length > 0 && panelSizeMm < 1) {
    throw new TypeError('パネルの一辺の長さは 1 以上にしてください。');
  }
  return {
    version: parseVersion(value.version, 'マップの様子'),
    mapVersion,
    layout: { panelSizeMm, panels, connections: list(value.connections, 'つながり').map(parseConnection) },
    equipment: list(value.equipment, '装置とステーション').map(parseEquipment),
  };
}
