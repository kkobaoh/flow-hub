import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { MapCanvas } from './MapCanvas';
import { sampleLayout } from './sampleLayout';

function count(html: string, pattern: RegExp): number {
  return html.match(pattern)?.length ?? 0;
}

describe('MapCanvas', () => {
  const html = renderToStaticMarkup(<MapCanvas layout={sampleLayout} />);

  it('マップを、名前の付いた1枚の絵として描く', () => {
    expect(html).toContain('<svg');
    expect(html).toContain('role="img"');
    expect(html).toContain('aria-label="マップ"');
  });

  it('描く範囲は、マップの大きさ（mm）', () => {
    expect(html).toContain('viewBox="0 0 6000 4000"');
  });

  it('パネルを1枚ずつ描く', () => {
    expect(count(html, /data-panel="/g)).toBe(sampleLayout.panels.length);
  });

  it('穴、停止ライン、充電ピン、下ろす角度の矢印を描く', () => {
    expect(count(html, /data-hole="square"/g)).toBe(3);
    expect(count(html, /data-hole="round"/g)).toBe(1);
    expect(count(html, /data-stop-bar/g)).toBe(2);
    expect(count(html, /data-pin/g)).toBe(2);
    expect(count(html, /data-lowering-arrow/g)).toBe(1);
  });

  it('パネルの名前を描く', () => {
    expect(html).toContain('>SB2<');
  });
});
