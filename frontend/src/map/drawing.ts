import type { Heading, MapLayout, Panel, PanelId, PanelType } from './model';

/** マップの上の位置（mm）。x は東、y は南へ増える。 */
export interface Point {
  readonly x: number;
  readonly y: number;
}

export interface Rect {
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
}

export interface Segment {
  readonly from: Point;
  readonly to: Point;
}

export interface Tile {
  readonly id: PanelId;
  readonly type: PanelType;
  readonly rect: Rect;
}

export interface Hole {
  readonly panel: PanelId;
  readonly shape: 'square' | 'round';
  readonly center: Point;
  readonly sizeMm: number;
}

export interface ChargingPin {
  readonly panel: PanelId;
  readonly center: Point;
  readonly sizeMm: number;
}

export interface LoweringArrow {
  readonly panel: PanelId;
  readonly center: Point;
  readonly heading: Heading;
}

/** マップを描くための形（すべて mm）。描き方の判断はここで済ませ、部品は描くだけにする。 */
export interface MapDrawing {
  readonly widthMm: number;
  readonly heightMm: number;
  readonly tiles: readonly Tile[];
  /** 白いライン（通常パネルの十字と、つながり）。 */
  readonly lines: readonly Segment[];
  /** 待機パネルの停止ライン。 */
  readonly stopBars: readonly Segment[];
  readonly holes: readonly Hole[];
  readonly pins: readonly ChargingPin[];
  readonly loweringArrows: readonly LoweringArrow[];
}

// パネルの一辺に対する大きさの割合（デザインの試作に合わせた値）
const HOLE_RATIO = 0.36;
const STOP_BAR_RATIO = 0.5;
const PIN_FORWARD_RATIO = 0.36;
const PIN_SIDE_RATIO = 0.18;
const PIN_SIZE_RATIO = 0.14;

/** 十字の位置。パネルの真ん中に、十字のずれを足したところ。 */
export function crossPoint(panel: Panel, panelSizeMm: number): Point {
  return {
    x: (panel.column + 0.5) * panelSizeMm + panel.crossOffsetMm.x,
    y: (panel.row + 0.5) * panelSizeMm + panel.crossOffsetMm.y,
  };
}

function panelRect(panel: Panel, size: number): Rect {
  return { x: panel.column * size, y: panel.row * size, width: size, height: size };
}

function unit(from: Point, to: Point): Point {
  const dx = to.x - from.x;
  const dy = to.y - from.y;
  const length = Math.hypot(dx, dy);
  return length === 0 ? { x: 0, y: 0 } : { x: dx / length, y: dy / length };
}

export function drawMap(layout: MapLayout): MapDrawing {
  const size = layout.panelSizeMm;
  const byId = new Map(layout.panels.map((p) => [p.id, p] as const));
  const panelOf = (id: PanelId): Panel => {
    const found = byId.get(id);
    if (!found) {
      throw new Error(`つながりが、マップにないパネル（${id}）を指しています。`);
    }
    return found;
  };
  const connections = layout.connections.map((c) => ({ from: panelOf(c.from), to: panelOf(c.to) }));

  const tiles: Tile[] = [];
  const lines: Segment[] = [];
  const stopBars: Segment[] = [];
  const holes: Hole[] = [];
  const pins: ChargingPin[] = [];
  const loweringArrows: LoweringArrow[] = [];
  let columns = 0;
  let rows = 0;

  for (const panel of layout.panels) {
    const rect = panelRect(panel, size);
    const cross = crossPoint(panel, size);
    columns = Math.max(columns, panel.column + 1);
    rows = Math.max(rows, panel.row + 1);
    tiles.push({ id: panel.id, type: panel.type, rect });

    switch (panel.type) {
      case 'Normal':
        lines.push({ from: { x: rect.x, y: cross.y }, to: { x: rect.x + size, y: cross.y } });
        lines.push({ from: { x: cross.x, y: rect.y }, to: { x: cross.x, y: rect.y + size } });
        break;
      case 'Transfer':
        holes.push({ panel: panel.id, shape: 'square', center: cross, sizeMm: size * HOLE_RATIO });
        break;
      case 'SpecialTransfer':
        holes.push({ panel: panel.id, shape: 'round', center: cross, sizeMm: size * HOLE_RATIO });
        if (panel.carrierLoweringHeading !== undefined) {
          loweringArrows.push({ panel: panel.id, center: cross, heading: panel.carrierLoweringHeading });
        }
        break;
      case 'Standby':
        break;
    }

    // 行き止まり（待機・受け渡し）：入ってくる向きに合わせて、停止ラインと充電ピンを描く
    if (panel.type === 'Standby' || panel.type === 'Transfer') {
      const link = connections.find((c) => c.from === panel || c.to === panel);
      if (link) {
        const neighbor = link.from === panel ? link.to : link.from;
        const dir = unit(crossPoint(neighbor, size), cross);
        const side = { x: -dir.y, y: dir.x };
        if (panel.type === 'Standby') {
          const half = (size * STOP_BAR_RATIO) / 2;
          stopBars.push({
            from: { x: cross.x - side.x * half, y: cross.y - side.y * half },
            to: { x: cross.x + side.x * half, y: cross.y + side.y * half },
          });
        }
        if (panel.hasChargingPin) {
          for (const sign of [-1, 1]) {
            pins.push({
              panel: panel.id,
              center: {
                x: cross.x + dir.x * size * PIN_FORWARD_RATIO + side.x * size * PIN_SIDE_RATIO * sign,
                y: cross.y + dir.y * size * PIN_FORWARD_RATIO + side.y * size * PIN_SIDE_RATIO * sign,
              },
              sizeMm: size * PIN_SIZE_RATIO,
            });
          }
        }
      }
    }
  }

  for (const c of connections) {
    lines.push({ from: crossPoint(c.from, size), to: crossPoint(c.to, size) });
  }

  return { widthMm: columns * size, heightMm: rows * size, tiles, lines, stopBars, holes, pins, loweringArrows };
}
