import { describe, expect, it } from 'vitest';
import { crossPoint, drawMap } from './drawing';
import { heading, panelId, type Connection, type MapLayout, type Panel } from './model';

const SIZE = 1000;

function panel(id: string, column: number, row: number, extra: Partial<Panel> = {}): Panel {
  return { id: panelId(id), type: 'Normal', hasChargingPin: false, column, row, crossOffsetMm: { x: 0, y: 0 }, ...extra };
}

function link(from: string, to: string): Connection {
  return { from: panelId(from), to: panelId(to), distanceMm: SIZE, heading: heading(90), oneWay: false };
}

function layout(panels: Panel[], connections: Connection[] = []): MapLayout {
  return { panelSizeMm: SIZE, panels, connections };
}

describe('crossPoint', () => {
  it('ずれがなければ、パネルの真ん中', () => {
    expect(crossPoint(panel('P1', 2, 1), SIZE)).toEqual({ x: 2500, y: 1500 });
  });

  it('ずれがあれば、真ん中から東と南へずらす', () => {
    expect(crossPoint(panel('P1', 2, 1, { crossOffsetMm: { x: 100, y: -50 } }), SIZE)).toEqual({ x: 2600, y: 1450 });
  });
});

describe('drawMap', () => {
  it('描く大きさは、一番東と一番南のパネルまで', () => {
    const d = drawMap(layout([panel('A', 0, 0), panel('B', 3, 2)]));

    expect(d.widthMm).toBe(4000);
    expect(d.heightMm).toBe(3000);
  });

  it('パネルは、格子の位置に一辺の長さの四角で描く', () => {
    const d = drawMap(layout([panel('A', 1, 2)]));

    expect(d.tiles).toEqual([{ id: 'A', type: 'Normal', rect: { x: 1000, y: 2000, width: 1000, height: 1000 } }]);
  });

  it('通常パネルには、十字を通る縦と横のラインを、パネルの端から端まで描く', () => {
    const d = drawMap(layout([panel('A', 0, 0, { crossOffsetMm: { x: 100, y: 0 } })]));

    expect(d.lines).toContainEqual({ from: { x: 0, y: 500 }, to: { x: 1000, y: 500 } });
    expect(d.lines).toContainEqual({ from: { x: 600, y: 0 }, to: { x: 600, y: 1000 } });
  });

  it('つながりは、十字と十字を結んで描く（ずれがあれば斜めになる）', () => {
    const d = drawMap(layout(
      [panel('A', 0, 0), panel('B', 1, 0, { crossOffsetMm: { x: 0, y: 200 } })],
      [link('A', 'B')],
    ));

    expect(d.lines).toContainEqual({ from: { x: 500, y: 500 }, to: { x: 1500, y: 700 } });
  });

  it('通常パネル以外には、十字のラインを描かない', () => {
    const d = drawMap(layout([panel('S', 0, 0, { type: 'Standby' })]));

    expect(d.lines).toEqual([]);
  });

  it('待機パネルには、十字に、進行方向と直角の停止ラインを描く（長さはパネルの半分）', () => {
    const d = drawMap(layout(
      [panel('P', 0, 0), panel('S', 1, 0, { type: 'Standby' })],
      [link('P', 'S')],
    ));

    expect(d.stopBars).toEqual([{ from: { x: 1500, y: 250 }, to: { x: 1500, y: 750 } }]);
  });

  it('充電ピンは、停止ラインより進行方向の奥に、ラインをはさんで2つ描く', () => {
    const d = drawMap(layout(
      [panel('P', 0, 0), panel('S', 1, 0, { type: 'Standby', hasChargingPin: true })],
      [link('P', 'S')],
    ));

    expect(d.pins).toHaveLength(2);
    for (const pin of d.pins) {
      expect(pin.center.x).toBeGreaterThan(1500);
    }
    expect(new Set(d.pins.map((p) => Math.sign(p.center.y - 500)))).toEqual(new Set([-1, 1]));
  });

  it('充電ピンのない待機パネルには、ピンを描かない', () => {
    const d = drawMap(layout(
      [panel('P', 0, 0), panel('S', 1, 0, { type: 'Standby' })],
      [link('P', 'S')],
    ));

    expect(d.pins).toEqual([]);
  });

  it('受け渡しパネルには四角い穴、特殊消耗品受け渡しパネルには丸い穴を、十字に描く', () => {
    const d = drawMap(layout([
      panel('T', 0, 0, { type: 'Transfer' }),
      panel('X', 1, 0, { type: 'SpecialTransfer', carrierLoweringHeading: heading(45) }),
    ]));

    expect(d.holes).toEqual([
      { panel: 'T', shape: 'square', center: { x: 500, y: 500 }, sizeMm: 360 },
      { panel: 'X', shape: 'round', center: { x: 1500, y: 500 }, sizeMm: 360 },
    ]);
  });

  it('特殊消耗品受け渡しパネルには、キャリアを下ろす角度の矢印を描く', () => {
    const d = drawMap(layout([panel('X', 0, 0, { type: 'SpecialTransfer', carrierLoweringHeading: heading(45) })]));

    expect(d.loweringArrows).toEqual([{ panel: 'X', center: { x: 500, y: 500 }, heading: 45 }]);
  });

  it('知らないパネルを指すつながりは、契約違反として例外にする', () => {
    expect(() => drawMap(layout([panel('A', 0, 0)], [link('A', 'Z')]))).toThrow(Error);
  });
});
