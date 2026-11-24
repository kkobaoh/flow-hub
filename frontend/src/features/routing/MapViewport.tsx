import {
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type MouseEvent as ReactMouseEvent,
  type PointerEvent as ReactPointerEvent,
  type ReactNode,
  type RefObject,
} from 'react';
import { drawMap } from './drawing';
import { contentBounds } from '../tool/equipment';
import { unplacedRow } from './unplaced';
import { MapCanvas, type MapCanvasProps } from './MapCanvas';
import {
  fitView,
  panBy,
  pinchView,
  reveal,
  viewBoxOf,
  type Obstruction,
  zoomAt,
  zoomLimits,
  type PinchPoint,
  type ScreenPoint,
  type Size,
  type View,
  type ZoomLimits,
} from './viewport';

const NOT_COVERED: Obstruction = { left: 0, bottom: 0 };

/** ここまでの動きは、ドラッグではなく押したことにする（px）。 */
const DRAG_THRESHOLD_PX = 4;
const BUTTON_ZOOM = 1.25;
const WHEEL_ZOOM = 1.1;

export interface MapViewportProps extends Omit<MapCanvasProps, 'viewBox'> {
  /** 画面の大きさ（px）。なければ、置かれた場所の大きさを測る。 */
  readonly size?: Size;
  /**
   * 隠したくないマップの点（mm）。カードを開いた AGV など。覆われていれば、見えるところまで動かす。
   * null に戻ったら、最初に動かす前の位置に戻す（そのあいだに、自分でマップを動かしていなければ）。
   */
  readonly focus?: { readonly x: number; readonly y: number } | null;
  /** 画面のうち、カードなどに覆われているところ。 */
  readonly covered?: Obstruction;
  /** 右端のボタンの列の一番上に足すボタン（一覧のボタンなど）。 */
  readonly controls?: ReactNode;
}

interface RevealMemory {
  /** 最初に動かす前の見え方（null は「全体が収まる大きさ」）。 */
  readonly before: View | null;
  /** 動かしたあとに、自分でマップを動かしたか（そうなら戻さない）。 */
  moved: boolean;
}

interface Gesture {
  readonly pointers: Map<number, ScreenPoint>;
  /** ドラッグかつまむ操作をしたか（したら、指を離したところを押したことにしない）。 */
  dragged: boolean;
  drag: { readonly start: ScreenPoint; readonly view: View } | null;
  pinch: { readonly from: PinchPoint; readonly view: View } | null;
}

/**
 * マップを、ドラッグで動かし、ホイール・つまむ操作・ボタンで拡大・縮小して見せる。
 * 見え方の計算は viewport.ts の関数が行い、ここでは操作をその関数に渡すだけにする。
 */
export function MapViewport({ size: fixedSize, focus = null, covered = NOT_COVERED, controls, ...canvas }: MapViewportProps) {
  const surfaceRef = useRef<HTMLDivElement>(null);
  const size = useMeasuredSize(surfaceRef, fixedSize);
  const drawing = useMemo(() => drawMap(canvas.layout), [canvas.layout]);
  const unplacedCount = canvas.unplacedAgvs?.length ?? 0;
  const bounds = useMemo(
    () => unplacedRow(contentBounds(drawing, canvas.equipment ?? []), canvas.layout.panelSizeMm, unplacedCount).bounds,
    [drawing, canvas.equipment, canvas.layout.panelSizeMm, unplacedCount],
  );
  const fit = size ? fitView(bounds, size) : null;
  const limits = fit ? zoomLimits(fit.scale, canvas.layout.panelSizeMm) : null;
  // null のあいだは「全体が収まる大きさ」。画面の大きさが変わっても、合わせて収め直す。
  const [view, setView] = useState<View | null>(null);
  const current = view ?? fit;

  // 続けて届く操作（ホイールなど）が、描き直す前の見え方を使わないように、最新の見え方を持っておく
  const live = useRef({ view: current, size, limits });
  live.current = { view: current, size, limits };
  const gesture = useRef<Gesture>({ pointers: new Map(), dragged: false, drag: null, pinch: null });

  const revealed = useRef<RevealMemory | null>(null);

  /** ユーザーの操作で見え方を変える（動かしたあとなら、focus がなくなっても戻さない）。 */
  const update = (next: (view: View, size: Size, limits: ZoomLimits) => View) => {
    const { view: v, size: s, limits: l } = live.current;
    if (!v || !s || !l) {
      return;
    }
    const nextView = next(v, s, l);
    live.current = { ...live.current, view: nextView };
    if (revealed.current) {
      revealed.current.moved = true;
    }
    setView(nextView);
  };

  const focusX = focus?.x;
  const focusY = focus?.y;
  useEffect(() => {
    const { view: v, size: s } = live.current;
    if (focusX === undefined || focusY === undefined) {
      const memory = revealed.current;
      revealed.current = null;
      if (memory && !memory.moved) {
        setView(memory.before);
      }
      return;
    }
    if (!v || !s) {
      return;
    }
    const next = reveal(v, s, { x: focusX, y: focusY }, covered);
    if (!revealed.current) {
      revealed.current = { before: view, moved: false };
    }
    if (next) {
      live.current = { ...live.current, view: next };
      setView(next);
    }
    // 点が変わったときだけ動かす（覆われ方や見え方が変わっただけでは動かさない）
  }, [focusX, focusY]);

  useEffect(() => {
    const el = surfaceRef.current;
    if (!el) {
      return undefined;
    }
    // ホイールでページがスクロールしないように、受け身でない受け取り方にする
    const onWheel = (e: WheelEvent) => {
      e.preventDefault();
      const at = relative(el, e.clientX, e.clientY);
      update((v, s, l) => zoomAt(v, s, at, e.deltaY < 0 ? WHEEL_ZOOM : 1 / WHEEL_ZOOM, l));
    };
    el.addEventListener('wheel', onWheel, { passive: false });
    return () => el.removeEventListener('wheel', onWheel);
  });

  const onPointerDown = (e: ReactPointerEvent<HTMLDivElement>) => {
    const g = gesture.current;
    const at = relative(e.currentTarget, e.clientX, e.clientY);
    g.pointers.set(e.pointerId, at);
    const v = live.current.view;
    if (!v) {
      return;
    }
    if (g.pointers.size === 1) {
      g.dragged = false;
      g.drag = { start: at, view: v };
      g.pinch = null;
    } else if (g.pointers.size === 2) {
      g.dragged = true;
      g.drag = null;
      g.pinch = { from: pinchPoint(g.pointers), view: v };
    }
  };

  const onPointerMove = (e: ReactPointerEvent<HTMLDivElement>) => {
    const g = gesture.current;
    if (!g.pointers.has(e.pointerId)) {
      return;
    }
    const at = relative(e.currentTarget, e.clientX, e.clientY);
    g.pointers.set(e.pointerId, at);
    const { pinch, drag } = g;
    if (pinch && g.pointers.size >= 2) {
      update((_, s, l) => pinchView(pinch.view, s, pinch.from, pinchPoint(g.pointers), l));
      return;
    }
    if (drag) {
      const delta = { x: at.x - drag.start.x, y: at.y - drag.start.y };
      if (!g.dragged && Math.hypot(delta.x, delta.y) > DRAG_THRESHOLD_PX) {
        g.dragged = true;
      }
      if (g.dragged) {
        update(() => panBy(drag.view, delta));
      }
    }
  };

  const onPointerEnd = (e: ReactPointerEvent<HTMLDivElement>) => {
    const g = gesture.current;
    if (!g.pointers.delete(e.pointerId)) {
      return;
    }
    if (g.pointers.size < 2) {
      g.pinch = null;
    }
    const rest = [...g.pointers.values()][0];
    const v = live.current.view;
    g.drag = rest && v ? { start: rest, view: v } : null;
  };

  // ドラッグやつまむ操作のあとは、指を離したところ（AGV など）を押したことにしない
  const onClickCapture = (e: ReactMouseEvent<HTMLDivElement>) => {
    if (gesture.current.dragged) {
      e.stopPropagation();
      e.preventDefault();
    }
  };

  const zoomFromCenter = (factor: number) =>
    update((v, s, l) => zoomAt(v, s, { x: s.width / 2, y: s.height / 2 }, factor, l));

  return (
    <div className="relative h-full w-full overflow-hidden">
      <div
        ref={surfaceRef}
        data-map-surface=""
        className="h-full w-full cursor-grab touch-none select-none active:cursor-grabbing"
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerEnd}
        onPointerCancel={onPointerEnd}
        onPointerLeave={onPointerEnd}
        onClickCapture={onClickCapture}
      >
        {current && size && <MapCanvas {...canvas} viewBox={viewBoxOf(current, size)} />}
      </div>
      <div data-map-controls="" className="absolute top-1/2 right-3 flex -translate-y-1/2 flex-col items-center gap-2">
        {controls}
        {controls && <div aria-hidden="true" className="h-px w-7 bg-control-border" />}
        <ZoomButton label="拡大する" onClick={() => zoomFromCenter(BUTTON_ZOOM)}>
          <path d="M12 5v14" />
          <path d="M5 12h14" />
        </ZoomButton>
        <ZoomButton label="縮小する" onClick={() => zoomFromCenter(1 / BUTTON_ZOOM)}>
          <path d="M5 12h14" />
        </ZoomButton>
        <ZoomButton
          label="表示の位置を戻す"
          onClick={() => {
            live.current = { ...live.current, view: fit };
            if (revealed.current) {
              revealed.current.moved = true;
            }
            setView(null);
          }}
        >
          <circle cx="12" cy="12" r="7" />
          <circle cx="12" cy="12" r="2" />
          <path d="M12 2v3" />
          <path d="M12 19v3" />
          <path d="M2 12h3" />
          <path d="M19 12h3" />
        </ZoomButton>
      </div>
    </div>
  );
}

function ZoomButton({ label, onClick, children }: { label: string; onClick: () => void; children: ReactNode }) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      onClick={onClick}
      className="flex size-11 items-center justify-center rounded-lg border border-control-border bg-white text-graphite focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-focus"
    >
      <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
        {children}
      </svg>
    </button>
  );
}

function relative(el: Element, clientX: number, clientY: number): ScreenPoint {
  const rect = el.getBoundingClientRect();
  return { x: clientX - rect.left, y: clientY - rect.top };
}

function pinchPoint(pointers: ReadonlyMap<number, ScreenPoint>): PinchPoint {
  const [a, b] = [...pointers.values()];
  if (!a || !b) {
    throw new Error('つまむ操作には、2本の指が要ります。');
  }
  return { mid: { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 }, distance: Math.hypot(a.x - b.x, a.y - b.y) };
}

/** 置かれた場所の大きさを測る。fixed があれば、それを使う（テストや Storybook のため）。 */
function useMeasuredSize(ref: RefObject<HTMLElement | null>, fixed: Size | undefined): Size | null {
  const [measured, setMeasured] = useState<Size | null>(null);
  useLayoutEffect(() => {
    const el = ref.current;
    if (fixed || !el || typeof ResizeObserver === 'undefined') {
      return undefined;
    }
    const observer = new ResizeObserver(([entry]) => {
      if (entry) {
        setMeasured({ width: entry.contentRect.width, height: entry.contentRect.height });
      }
    });
    observer.observe(el);
    return () => observer.disconnect();
  }, [ref, fixed]);
  return fixed ?? measured;
}
