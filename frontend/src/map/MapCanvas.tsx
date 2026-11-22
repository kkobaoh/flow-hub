import { useMemo, type KeyboardEvent } from 'react';
import { indicatorLight, lightLabel, type AgvId, type AgvOnMap, type LightColor } from './agv';
import { crossPoint, drawMap, panelOf, routeSegments } from './drawing';
import { attentionTag, contentBounds, labelSide, textWidthMm, type EquipmentId, type EquipmentOnMap } from './equipment';
import type { MapLayout, PanelId } from './model';
import type { ViewBox } from './viewport';

const LIGHT_FILL: Record<LightColor, string> = {
  moving: 'fill-light-moving',
  waiting: 'fill-light-waiting',
  charging: 'fill-light-charging',
  chargeFailed: 'fill-light-charge-failed',
};

// AGV の印の大きさ（パネルの一辺に対する割合）。AGV の画像に差し替えるまでの仮の印。
const AGV_RADIUS_RATIO = 0.28;
const RESERVED_RATIO = 0.05;

// パネルの一辺に対する大きさの割合（デザインの試作に合わせた値）
const LINE_RATIO = 0.08;
const STOP_BAR_RATIO = 0.06;
const LABEL_RATIO = 0.17;
const ARROW_RATIO = 0.3;

export interface MapCanvasProps {
  readonly layout: MapLayout;
  readonly agvs?: readonly AgvOnMap[];
  /** 予約しているパネル。 */
  readonly reservedPanels?: readonly PanelId[];
  /** 走る予定の経路（通るパネルの順）。AGV ごとに1つ。 */
  readonly routes?: readonly (readonly PanelId[])[];
  /** 選んでいる AGV（カードを開いている AGV）。 */
  readonly selectedAgv?: AgvId | null;
  /** 一覧などでマウスを乗せている AGV。 */
  readonly highlightedAgv?: AgvId | null;
  readonly onSelectAgv?: (id: AgvId) => void;
  /** マウスを乗せた AGV を知らせる（外したら null）。 */
  readonly onHighlightAgv?: (id: AgvId | null) => void;
  /** 見えている範囲（mm）。なければ、マップの全体。 */
  readonly viewBox?: ViewBox | undefined;
  /** 装置とステーション（受け渡しパネルの真下にあるので、パネルに重ねて透かして描く）。 */
  readonly equipment?: readonly EquipmentOnMap[];
  readonly selectedEquipment?: EquipmentId | null;
  readonly highlightedEquipment?: EquipmentId | null;
  readonly onSelectEquipment?: (id: EquipmentId) => void;
  readonly onHighlightEquipment?: (id: EquipmentId | null) => void;
}

/**
 * マップを、実物と同じ見た目（黒いパネルに白いライン）と縮尺で描く。
 * 描く位置の判断は drawMap が済ませるので、ここでは描くだけにする。
 */
export function MapCanvas({
  layout,
  agvs = [],
  reservedPanels = [],
  routes = [],
  selectedAgv = null,
  highlightedAgv = null,
  onSelectAgv,
  onHighlightAgv,
  viewBox,
  equipment = [],
  selectedEquipment = null,
  highlightedEquipment = null,
  onSelectEquipment,
  onHighlightEquipment,
}: MapCanvasProps) {
  const d = useMemo(() => drawMap(layout), [layout]);
  const size = layout.panelSizeMm;
  const line = size * LINE_RATIO;
  const arrow = size * ARROW_RATIO;
  const head = arrow * 0.35;
  const reservedRects = reservedPanels.map((id) => d.tiles.find((t) => t.id === id) ?? panelMissing(id));
  const routeLines = routes.flatMap((r) => routeSegments(layout, r));
  const markers = agvs.map((agv) => {
    const panel = panelOf(layout, agv.panel);
    const light = indicatorLight(agv.status, panel.hasChargingPin);
    const emphasis = agv.id === selectedAgv ? 'selected' : agv.id === highlightedAgv ? 'highlighted' : 'none';
    return { agv, center: crossPoint(panel, size), light, emphasis } as const;
  });
  const radius = size * AGV_RADIUS_RATIO;
  const select = (id: AgvId) => onSelectAgv?.(id);
  const activateKey = (run: () => void) => (e: KeyboardEvent<SVGGElement>) => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      run();
    }
  };
  const onKey = (id: AgvId) => activateKey(() => select(id));
  const bounds = contentBounds(d, equipment);
  const nameFont = size * 0.15;
  const tagFont = size * 0.11;
  const chipPad = size * 0.04;
  const equipmentShapes = equipment.map((e) => {
    const panel = d.tiles.find((t) => t.id === e.panel) ?? panelMissing(e.panel);
    const side = labelSide(e.outlineMm, panel.rect);
    const tags = e.attentions.map(attentionTag);
    const emphasis = e.id === selectedEquipment ? 'selected' : e.id === highlightedEquipment ? 'highlighted' : 'none';
    // 札は、外形のうちパネルからはみ出した側に、上から（下の側なら下から）積む
    const nameHeight = nameFont + chipPad * 2;
    const tagHeight = tagFont + chipPad * 2;
    const gap = size * 0.03;
    const inset = size * 0.06;
    const chips = [
      { text: e.id as string, font: nameFont, height: nameHeight, tag: false },
      ...tags.map((text) => ({ text, font: tagFont, height: tagHeight, tag: true })),
    ];
    let cursor = side === 'top' ? e.outlineMm.y + inset : e.outlineMm.y + e.outlineMm.height - inset;
    const placed = (side === 'top' ? chips : [...chips].reverse()).map((c) => {
      const y = side === 'top' ? cursor : cursor - c.height;
      cursor = side === 'top' ? cursor + c.height + gap : cursor - c.height - gap;
      return { ...c, x: e.outlineMm.x + inset, y, width: textWidthMm(c.text, c.font) + chipPad * 2 };
    });
    return { e, tags, emphasis, chips: side === 'top' ? placed : placed.reverse() } as const;
  });

  return (
    <svg
      role="img"
      aria-label="マップ"
      viewBox={
        viewBox
          ? `${viewBox.x} ${viewBox.y} ${viewBox.width} ${viewBox.height}`
          : `${bounds.x} ${bounds.y} ${bounds.width} ${bounds.height}`
      }
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
      {reservedRects.map((t) => (
        <rect
          key={`r${t.id}`}
          data-reserved=""
          x={t.rect.x + (size * RESERVED_RATIO) / 2}
          y={t.rect.y + (size * RESERVED_RATIO) / 2}
          width={t.rect.width - size * RESERVED_RATIO}
          height={t.rect.height - size * RESERVED_RATIO}
          className="fill-none stroke-route"
          strokeWidth={size * RESERVED_RATIO}
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
      <g className="stroke-route" strokeLinecap="round">
        {routeLines.map((s, i) => (
          <line key={`rt${i}`} data-route="" x1={s.from.x} y1={s.from.y} x2={s.to.x} y2={s.to.y} strokeWidth={line} />
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
      {equipmentShapes.map(({ e, tags, emphasis, chips }) => (
        <g
          key={e.id}
          data-equipment={e.id}
          data-emphasis={emphasis}
          role="button"
          tabIndex={0}
          aria-label={tags.length > 0 ? `${e.id}（${tags.join('、')}）` : e.id}
          aria-pressed={emphasis === 'selected'}
          className="cursor-pointer outline-none"
          onClick={() => onSelectEquipment?.(e.id)}
          onKeyDown={activateKey(() => onSelectEquipment?.(e.id))}
          onMouseEnter={() => onHighlightEquipment?.(e.id)}
          onMouseLeave={() => onHighlightEquipment?.(null)}
        >
          <rect
            x={e.outlineMm.x}
            y={e.outlineMm.y}
            width={e.outlineMm.width}
            height={e.outlineMm.height}
            rx={size * 0.04}
            className="fill-none stroke-graphite"
            strokeWidth={size * 0.05}
          />
          <rect
            data-outline=""
            x={e.outlineMm.x}
            y={e.outlineMm.y}
            width={e.outlineMm.width}
            height={e.outlineMm.height}
            rx={size * 0.04}
            className={`fill-paper/5 ${emphasis === 'none' ? 'stroke-paper' : 'stroke-selection'}`}
            strokeWidth={size * (emphasis === 'selected' ? 0.05 : 0.03)}
            strokeDasharray={`${size * 0.08} ${size * 0.05}`}
          />
          {chips.map((c) => (
            <g key={c.text} {...(c.tag ? { 'data-tag': '' } : {})}>
              <rect x={c.x} y={c.y} width={c.width} height={c.height} rx={size * 0.03} className={c.tag ? 'fill-attention' : 'fill-paper'} />
              <text
                x={c.x + chipPad}
                y={c.y + c.height / 2}
                dominantBaseline="central"
                fontSize={c.font}
                className={c.tag ? 'fill-white font-sans font-bold' : 'fill-graphite font-number font-bold'}
              >
                {c.text}
              </text>
            </g>
          ))}
        </g>
      ))}
      {markers.map(({ agv, center, light, emphasis }) => (
        <g
          key={agv.id}
          data-agv={agv.id}
          data-emphasis={emphasis}
          role="button"
          tabIndex={0}
          aria-label={`${agv.id}（${lightLabel(light.color)}）`}
          aria-pressed={emphasis === 'selected'}
          transform={`translate(${center.x} ${center.y}) rotate(${agv.heading})`}
          className="cursor-pointer outline-none"
          onClick={() => select(agv.id)}
          onKeyDown={onKey(agv.id)}
          onMouseEnter={() => onHighlightAgv?.(agv.id)}
          onMouseLeave={() => onHighlightAgv?.(null)}
        >
          {emphasis !== 'none' && (
            <circle
              r={radius * 1.35}
              className="fill-none stroke-selection"
              strokeWidth={size * 0.05}
              strokeDasharray={emphasis === 'highlighted' ? `${size * 0.08} ${size * 0.06}` : undefined}
            />
          )}
          <circle r={radius} className="fill-agv-body stroke-panel-line" strokeWidth={size * 0.03} />
          {/* 向き（0度＝北を上にして描き、全体を向きの角度だけ回す） */}
          <path
            d={`M 0 ${-radius * 0.75} L ${radius * 0.32} ${-radius * 0.3} L ${-radius * 0.32} ${-radius * 0.3} Z`}
            className="fill-panel-line"
          />
          <circle
            data-light={light.color}
            cx={radius * 0.72}
            cy={-radius * 0.72}
            r={radius * 0.3}
            className={`${LIGHT_FILL[light.color]} stroke-panel-line${light.blinking ? ' animate-agv-blink' : ''}`}
            strokeWidth={size * 0.02}
          />
          <text
            transform={`rotate(${-agv.heading})`}
            textAnchor="middle"
            dominantBaseline="central"
            y={0}
            fontSize={size * 0.22}
            className="fill-panel-line font-number font-bold"
          >
            {agv.label}
          </text>
        </g>
      ))}
    </svg>
  );
}

function panelMissing(id: PanelId): never {
  throw new Error(`マップにないパネル（${id}）を指しています。`);
}
