import { describe, expect, it } from 'vitest';
import { agvLabelFontRatio, unplacedRow } from './unplaced';

const content = { x: -500, y: -200, width: 6000, height: 4000 };

describe('unplacedRow（位置がわからない AGV の列）', () => {
  it('いなければ列を作らず、範囲はマップのまま', () => {
    const row = unplacedRow(content, 1000, 0);

    expect(row.slots).toEqual([]);
    expect(row.bounds).toEqual(content);
  });

  it('マップの一番下の外に見出しを置き、その下に左から横一列に並べる', () => {
    const row = unplacedRow(content, 1000, 3);

    expect(row.title.y).toBeGreaterThan(content.y + content.height);
    expect(row.title.x).toBe(content.x);
    expect(row.slots).toHaveLength(3);
    const [a, b, c] = row.slots;
    expect(a!.y).toBeGreaterThan(row.title.y);
    expect(new Set(row.slots.map((s) => s.y)).size).toBe(1); // 横一列
    expect(a!.x).toBeLessThan(b!.x);
    expect(b!.x - a!.x).toBeCloseTo(c!.x - b!.x);
  });

  it('全体の範囲は、列まで含める（全体を収めるときに列も見えるように）', () => {
    const row = unplacedRow(content, 1000, 2);

    expect(row.bounds.x).toBe(content.x);
    expect(row.bounds.y).toBe(content.y);
    expect(row.bounds.width).toBe(content.width);
    expect(row.bounds.y + row.bounds.height).toBeGreaterThan(row.slots[0]!.y + 280); // 印の下の端まで
  });

  it('マップより列が長ければ、範囲を右へ広げる', () => {
    const narrow = { x: 0, y: 0, width: 1000, height: 1000 };
    const row = unplacedRow(narrow, 1000, 5);

    expect(row.bounds.x + row.bounds.width).toBeGreaterThan(row.slots[4]!.x);
  });
});

describe('agvLabelFontRatio（印に書く名前の字の大きさ）', () => {
  it('短い名前は、決まった大きさ', () => {
    expect(agvLabelFontRatio('1')).toBe(0.22);
  });

  it('長い名前は、印の円に収まるように小さくする', () => {
    expect(agvLabelFontRatio('AGV-12')).toBeLessThan(0.22);
    expect(agvLabelFontRatio('AGV-12') * 6 * 0.6).toBeLessThanOrEqual(0.48 + 1e-9);
  });
});
