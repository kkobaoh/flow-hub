import { describe, expect, it } from 'vitest';
import { fitView, panBy, pinchView, reveal, toMap, viewBoxOf, zoomAt, zoomLimits, type Size, type View } from './viewport';

const screen: Size = { width: 1048, height: 648 };
// 6000 x 4000 mm のマップ。余白 24px を除いた 1000 x 600 px に収めると、0.15 px/mm
const map = { widthMm: 6000, heightMm: 4000 };

describe('fitView', () => {
  it('マップの全体が、余白を残して画面に収まる大きさにし、真ん中に置く', () => {
    expect(fitView(map, screen)).toEqual({ scale: 0.15, centerX: 3000, centerY: 2000 });
  });
});

describe('zoomLimits', () => {
  it('縮小は全体が収まる大きさの半分まで、拡大はパネル1枚が 200px になるまで', () => {
    expect(zoomLimits(0.15, 1000)).toEqual({ min: 0.075, max: 0.2 });
  });

  it('小さいマップで、全体が収まる大きさが 200px より大きいときは、それ以上拡大しない', () => {
    expect(zoomLimits(0.5, 1000)).toEqual({ min: 0.25, max: 0.5 });
  });
});

describe('viewBoxOf', () => {
  it('画面に見えている範囲を、マップの mm で表す', () => {
    const view: View = { scale: 0.1, centerX: 3000, centerY: 2000 };

    expect(viewBoxOf(view, { width: 1000, height: 600 })).toEqual({ x: -2000, y: -1000, width: 10000, height: 6000 });
  });
});

describe('toMap', () => {
  it('画面の点（左上から px）を、マップの点（mm）にする', () => {
    const view: View = { scale: 0.1, centerX: 3000, centerY: 2000 };

    expect(toMap(view, { width: 1000, height: 600 }, { x: 500, y: 300 })).toEqual({ x: 3000, y: 2000 });
    expect(toMap(view, { width: 1000, height: 600 }, { x: 600, y: 300 })).toEqual({ x: 4000, y: 2000 });
  });
});

describe('panBy', () => {
  it('ドラッグした向きに、マップを動かす', () => {
    const view: View = { scale: 0.1, centerX: 3000, centerY: 2000 };

    expect(panBy(view, { x: 100, y: -50 })).toEqual({ scale: 0.1, centerX: 2000, centerY: 2500 });
  });
});

describe('zoomAt', () => {
  const size = { width: 1000, height: 600 };
  const limits = { min: 0.05, max: 0.2 };

  it('指した点を動かさずに、拡大・縮小する', () => {
    const view: View = { scale: 0.1, centerX: 3000, centerY: 2000 };
    const point = { x: 800, y: 100 };
    const before = toMap(view, size, point);

    const zoomed = zoomAt(view, size, point, 1.5, limits);

    expect(zoomed.scale).toBeCloseTo(0.15);
    const after = toMap(zoomed, size, point);
    expect(after.x).toBeCloseTo(before.x);
    expect(after.y).toBeCloseTo(before.y);
  });

  it('限りを超えて拡大・縮小しない', () => {
    const view: View = { scale: 0.1, centerX: 3000, centerY: 2000 };

    expect(zoomAt(view, size, { x: 500, y: 300 }, 10, limits).scale).toBe(0.2);
    expect(zoomAt(view, size, { x: 500, y: 300 }, 0.01, limits).scale).toBe(0.05);
  });
});

describe('reveal（カードに隠れないように動かす）', () => {
  const size = { width: 1000, height: 600 };
  const view: View = { scale: 0.1, centerX: 3000, centerY: 2000 };
  // 画面の左上 (0,0) は、マップの (-2000, -1000)。マップの (x, y) は、画面の ((x + 2000) / 10, (y + 1000) / 10)

  it('見えていて、覆われていなければ、動かさない', () => {
    expect(reveal(view, size, { x: 5000, y: 2000 }, { left: 380, bottom: 0 })).toBeNull();
  });

  it('左のカードに覆われていたら、覆われていない範囲の真ん中に来るように動かす', () => {
    const point = { x: -1000, y: 2000 }; // 画面の (100, 300)。カード（幅 380px）の下

    const moved = reveal(view, size, point, { left: 380, bottom: 0 });

    expect(moved).not.toBeNull();
    const at = { x: (point.x - moved!.centerX) * 0.1 + 500, y: (point.y - moved!.centerY) * 0.1 + 300 };
    expect(at.x).toBeCloseTo((380 + 1000) / 2);
    expect(at.y).toBeCloseTo(300);
    expect(moved!.scale).toBe(0.1);
  });

  it('下のカード（スマートフォン）に覆われていたら、上に見えている範囲の真ん中に来るように動かす', () => {
    const point = { x: 3000, y: 4500 }; // 画面の (500, 550)

    const moved = reveal(view, size, point, { left: 0, bottom: 330 });

    const at = { x: (point.x - moved!.centerX) * 0.1 + 500, y: (point.y - moved!.centerY) * 0.1 + 300 };
    expect(at.x).toBeCloseTo(500);
    expect(at.y).toBeCloseTo((600 - 330) / 2);
  });

  it('見える範囲の外にあれば、動かす', () => {
    expect(reveal(view, size, { x: 20000, y: 2000 }, { left: 0, bottom: 0 })).not.toBeNull();
  });

  it('端に近すぎるとき（48px より近い）も、動かす', () => {
    // 画面の (420, 300)：カードの端（380px）から 40px
    expect(reveal(view, size, { x: 2200, y: 2000 }, { left: 380, bottom: 0 })).not.toBeNull();
  });
});

describe('pinchView', () => {
  const size = { width: 1000, height: 600 };
  const limits = { min: 0.05, max: 0.4 };

  it('指の間を広げた割合だけ拡大し、つまみ始めた点を、指の真ん中に付いてこさせる', () => {
    const start: View = { scale: 0.1, centerX: 3000, centerY: 2000 };
    const startMid = { x: 400, y: 300 };
    const grabbed = toMap(start, size, startMid);

    const view = pinchView(start, size, { mid: startMid, distance: 100 }, { mid: { x: 450, y: 320 }, distance: 200 }, limits);

    expect(view.scale).toBeCloseTo(0.2);
    const now = toMap(view, size, { x: 450, y: 320 });
    expect(now.x).toBeCloseTo(grabbed.x);
    expect(now.y).toBeCloseTo(grabbed.y);
  });
});
