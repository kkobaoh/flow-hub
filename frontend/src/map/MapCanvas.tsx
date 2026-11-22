import { useMemo } from 'react';
import { drawMap } from './drawing';
import type { MapLayout } from './model';

// パネルの一辺に対する大きさの割合（デザインの試作に合わせた値）
const LINE_RATIO = 0.08;
const STOP_BAR_RATIO = 0.06;
const LABEL_RATIO = 0.17;
const ARROW_RATIO = 0.3;

export interface MapCanvasProps {
  readonly layout: MapLayout;
}

/**
 * マップを、実物と同じ見た目（黒いパネルに白いライン）と縮尺で描く。
 * 描く位置の判断は drawMap が済ませるので、ここでは描くだけにする。
 */
export function MapCanvas({ layout }: MapCanvasProps) {
  const d = useMemo(() => drawMap(layout), [layout]);
  const size = layout.panelSizeMm;
  const line = size * LINE_RATIO;
  const arrow = size * ARROW_RATIO;
  const head = arrow * 0.35;

  return (
    <svg
      role="img"
      aria-label="マップ"
      viewBox={`0 0 ${d.widthMm} ${d.heightMm}`}
      className="block h-full w-full"
    >
      {d.tiles.map((t) => (
        <rect
          key={t.id}
          data-panel={t.id}
          x={t.rect.x}
          y={t.rect.y}
          width={t.rect.width}
          height={t.rect.height}
          className="fill-panel stroke-panel-edge"
          strokeWidth={size * 0.012}
        />
      ))}
      <g className="stroke-panel-line" strokeLinecap="square">
        {d.lines.map((s, i) => (
          <line key={`l${i}`} x1={s.from.x} y1={s.from.y} x2={s.to.x} y2={s.to.y} strokeWidth={line} />
        ))}
        {d.stopBars.map((s, i) => (
          <line key={`s${i}`} data-stop-bar="" x1={s.from.x} y1={s.from.y} x2={s.to.x} y2={s.to.y} strokeWidth={size * STOP_BAR_RATIO} />
        ))}
      </g>
      {d.pins.map((pin, i) => (
        <rect
          key={`p${i}`}
          data-pin=""
          x={pin.center.x - pin.sizeMm / 2}
          y={pin.center.y - pin.sizeMm / 2}
          width={pin.sizeMm}
          height={pin.sizeMm}
          rx={pin.sizeMm * 0.15}
          className="fill-pin stroke-panel-line"
          strokeWidth={size * 0.014}
        />
      ))}
      {d.holes.map((h) =>
        h.shape === 'square' ? (
          <rect
            key={`h${h.panel}`}
            data-hole="square"
            x={h.center.x - h.sizeMm / 2}
            y={h.center.y - h.sizeMm / 2}
            width={h.sizeMm}
            height={h.sizeMm}
            className="fill-hole stroke-panel-line"
            strokeWidth={size * 0.028}
          />
        ) : (
          <circle
            key={`h${h.panel}`}
            data-hole="round"
            cx={h.center.x}
            cy={h.center.y}
            r={h.sizeMm / 2}
            className="fill-hole stroke-panel-line"
            strokeWidth={size * 0.028}
          />
        ),
      )}
      {d.loweringArrows.map((a) => (
        <g
          key={`a${a.panel}`}
          data-lowering-arrow=""
          transform={`rotate(${a.heading} ${a.center.x} ${a.center.y})`}
          className="fill-none stroke-lowering"
          strokeWidth={line * 0.7}
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          <path d={`M ${a.center.x} ${a.center.y} V ${a.center.y - arrow}`} />
          <path d={`M ${a.center.x - head} ${a.center.y - arrow + head} L ${a.center.x} ${a.center.y - arrow} L ${a.center.x + head} ${a.center.y - arrow + head}`} />
        </g>
      ))}
      {d.tiles.map((t) => (
        <text
          key={`t${t.id}`}
          x={t.rect.x + size * 0.07}
          y={t.rect.y + size * 0.07 + size * LABEL_RATIO * 0.8}
          fontSize={size * LABEL_RATIO}
          className="fill-panel-line font-number font-semibold"
        >
          {t.id}
        </text>
      ))}
    </svg>
  );
}
