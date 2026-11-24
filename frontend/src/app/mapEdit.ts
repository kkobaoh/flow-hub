import { crossPoint, type Rect } from '../map/drawing';
import { heading, panelId, type Connection, type Heading, type MapLayout, type Panel, type PanelId, type PanelType } from '../map/model';

/** 編集しているマップ（下書き）。外形は、装置とステーションの ID ごとに持つ（mm）。 */
export interface MapDraft {
  readonly layout: MapLayout;
  readonly outlines: Readonly<Record<string, Rect>>;
}

export const panelTypeLabel: Record<PanelType, string> = {
  Normal: '通常パネル',
  Transfer: '受け渡しパネル',
  Standby: '待機パネル',
  SpecialTransfer: '特殊消耗品受け渡しパネル',
};

const PREFIX: Record<PanelType, string> = { Normal: 'P', Transfer: 'D', Standby: 'SB', SpecialTransfer: 'SP' };

/** 置いたパネルの ID：種類ごとの頭文字に、まだ使っていないいちばん小さい番号を付ける。 */
export function nextPanelId(type: PanelType, panels: readonly Panel[]): string {
  const used = new Set<string>(panels.map((p) => p.id));
  let n = 1;
  while (used.has(`${PREFIX[type]}${n}`)) {
    n += 1;
  }
  return `${PREFIX[type]}${n}`;
}

const withLayout = (d: MapDraft, change: Partial<MapLayout>): MapDraft => ({ ...d, layout: { ...d.layout, ...change } });

/** 空いた格子にパネルを置く。パネルがあるところには置かない（同じ下書きを返す）。 */
export function placePanel(d: MapDraft, type: PanelType, column: number, row: number): MapDraft {
  if (d.layout.panels.some((p) => p.column === column && p.row === row)) {
    return d;
  }
  const added: Panel = { id: panelId(nextPanelId(type, d.layout.panels)), type, hasChargingPin: false, column, row, crossOffsetMm: { x: 0, y: 0 } };
  return withLayout(d, { panels: [...d.layout.panels, added] });
}

/** パネルを消す。そのパネルのつながりも消す。 */
export function removePanel(d: MapDraft, id: PanelId): MapDraft {
  return withLayout(d, {
    panels: d.layout.panels.filter((p) => p.id !== id),
    connections: d.layout.connections.filter((c) => c.from !== id && c.to !== id),
  });
}

export function updatePanel(d: MapDraft, id: PanelId, change: (p: Panel) => Panel): MapDraft {
  return withLayout(d, { panels: d.layout.panels.map((p) => (p.id === id ? change(p) : p)) });
}

const canHavePin = (type: PanelType) => type === 'Transfer' || type === 'Standby';

/** 種類を変える。その種類に付けられない充電ピンと、下ろす角度は外す。 */
export function setPanelType(d: MapDraft, id: PanelId, type: PanelType): MapDraft {
  return updatePanel(d, id, (p) => {
    const { carrierLoweringHeading, ...rest } = p;
    const keep = type === 'SpecialTransfer' && carrierLoweringHeading !== undefined ? { carrierLoweringHeading } : {};
    return { ...rest, ...keep, type, hasChargingPin: canHavePin(type) && p.hasChargingPin };
  });
}

/** 下ろす角度を入れる（null で外す）。 */
export function setLoweringHeading(d: MapDraft, id: PanelId, value: Heading | null): MapDraft {
  return updatePanel(d, id, (p) => {
    const { carrierLoweringHeading: _old, ...rest } = p;
    return value === null ? rest : { ...rest, carrierLoweringHeading: value };
  });
}

export type Refusable = { readonly kind: 'connected' | 'renamed'; readonly draft: MapDraft } | { readonly kind: 'refused'; readonly reason: string };

const find = (d: MapDraft, id: PanelId) => d.layout.panels.find((p) => p.id === id);

function degreesBetween(from: Panel, to: Panel, size: number): { readonly heading: Heading; readonly distanceMm: number } {
  const a = crossPoint(from, size);
  const b = crossPoint(to, size);
  const deg = Math.round((Math.atan2(b.x - a.x, -(b.y - a.y)) * 180) / Math.PI);
  return { heading: heading(((deg % 360) + 360) % 360), distanceMm: Math.round(Math.hypot(b.x - a.x, b.y - a.y)) };
}

/** 2枚のパネルをつなぐ。上下左右の隣だけ。向きと距離は十字の位置から計算し、一方通行にはしない。 */
export function connect(d: MapDraft, a: PanelId, b: PanelId): Refusable {
  const from = find(d, a);
  const to = find(d, b);
  if (from === undefined || to === undefined) {
    throw new RangeError(`下書きにないパネルです（${a}、${b}）。`);
  }
  if (Math.abs(from.column - to.column) + Math.abs(from.row - to.row) !== 1) {
    return { kind: 'refused', reason: `${a} と ${b} は、上下左右に隣り合っていないので、つなげません。` };
  }
  if (d.layout.connections.some((c) => (c.from === a && c.to === b) || (c.from === b && c.to === a))) {
    return { kind: 'refused', reason: `${a} と ${b} は、もうつながっています。` };
  }
  const added: Connection = { from: a, to: b, ...degreesBetween(from, to, d.layout.panelSizeMm), oneWay: false };
  return { kind: 'connected', draft: withLayout(d, { connections: [...d.layout.connections, added] }) };
}

const sameConnection = (x: Connection, y: Connection) => x.from === y.from && x.to === y.to;

export function removeConnection(d: MapDraft, c: Connection): MapDraft {
  return withLayout(d, { connections: d.layout.connections.filter((x) => !sameConnection(x, c)) });
}

const opposite = (h: number) => (h + 180) % 360;

/** 選んだパネルから見たつながり（相手と、相手へ進む向き）。 */
export function connectionView(c: Connection, from: PanelId): { readonly other: PanelId; readonly heading: number } {
  return c.from === from ? { other: c.to, heading: c.heading } : { other: c.from, heading: opposite(c.heading) };
}

/** つながりを直す。向きは、選んだパネルから見た向きで受け取る。 */
export function setConnection(
  d: MapDraft,
  c: Connection,
  viewFrom: PanelId,
  change: { readonly distanceMm?: number; readonly heading?: number; readonly oneWay?: boolean },
): MapDraft {
  return withLayout(d, {
    connections: d.layout.connections.map((x) => {
      if (!sameConnection(x, c)) {
        return x;
      }
      const h = change.heading === undefined ? x.heading : heading(viewFrom === x.from ? change.heading : opposite(change.heading));
      return { ...x, heading: h, distanceMm: change.distanceMm ?? x.distanceMm, oneWay: change.oneWay ?? x.oneWay };
    }),
  });
}

/** パネルの ID を直す。つながりの ID も直す。装置やステーションの受け渡しパネルは直せない。 */
export function renamePanel(d: MapDraft, id: PanelId, next: string, lockedIds: readonly string[]): Refusable {
  const n = next.trim();
  if (n === '') {
    return { kind: 'refused', reason: 'パネルの ID を入れてください。' };
  }
  if (lockedIds.includes(id)) {
    return { kind: 'refused', reason: `${id} は装置かステーションの受け渡しパネルなので、ID を直せません。` };
  }
  if (d.layout.panels.some((p) => p.id === n && p.id !== id)) {
    return { kind: 'refused', reason: `${n} は、もう使っています。` };
  }
  const to = panelId(n);
  const swap = (x: PanelId) => (x === id ? to : x);
  return {
    kind: 'renamed',
    draft: withLayout(d, {
      panels: d.layout.panels.map((p) => (p.id === id ? { ...p, id: to } : p)),
      connections: d.layout.connections.map((c) => ({ ...c, from: swap(c.from), to: swap(c.to) })),
    }),
  };
}

export interface GridBounds {
  readonly minColumn: number;
  readonly maxColumn: number;
  readonly minRow: number;
  readonly maxRow: number;
}

/** 編集する格子の広さ：パネルのまわりに1枚ずつ空けて広げる。パネルがなければ 6×4。 */
export function editBounds(panels: readonly Panel[]): GridBounds {
  if (panels.length === 0) {
    return { minColumn: 0, maxColumn: 5, minRow: 0, maxRow: 3 };
  }
  const cols = panels.map((p) => p.column);
  const rows = panels.map((p) => p.row);
  return { minColumn: Math.min(...cols) - 1, maxColumn: Math.max(...cols) + 1, minRow: Math.min(...rows) - 1, maxRow: Math.max(...rows) + 1 };
}

/** 保存した下書きと違うか。 */
export function isDraftChanged(saved: MapDraft, current: MapDraft): boolean {
  return JSON.stringify(saved) !== JSON.stringify(current);
}

export interface PublishInput {
  /** 検証した下書き（まだなら null）と、見つかった問題。 */
  readonly validated: MapDraft | null;
  readonly problems: readonly string[];
  readonly current: MapDraft;
  readonly running: boolean;
  readonly travelingAgvs: readonly string[];
  readonly allowed: boolean;
}

/** 公開を押せない理由。検証が通った下書きで、運転状態が停止、走っている AGV がないときだけ公開できる。 */
export function publishBlockedReason(input: PublishInput): string | null {
  if (!input.allowed) {
    return 'マップの編集・公開は、管理者のロールで使えます。';
  }
  if (input.validated === null) {
    return '先に「確かめる（検証）」を押してください。';
  }
  if (isDraftChanged(input.validated, input.current)) {
    return '検証のあとに変えたところがあります。もう一度確かめてください。';
  }
  if (input.problems.length > 0) {
    return '検証で見つかった問題を直すと、公開できます。';
  }
  if (input.running) {
    return '運転状態を停止にすると、公開できます。';
  }
  if (input.travelingAgvs.length > 0) {
    return `${input.travelingAgvs.join('、')} が走っているので、止まるまで公開できません。`;
  }
  return null;
}
