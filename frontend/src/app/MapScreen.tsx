import { useEffect, useState } from 'react';
import type { RunIndicator } from '../server/systemStatus';
import type { AgvId } from '../map/agv';
import { crossPoint, panelOf } from '../map/drawing';
import type { EquipmentId } from '../map/equipment';
import { MapViewport } from '../map/MapViewport';
import type { Heading, MapLayout, PanelId } from '../map/model';
import type { Obstruction, Size } from '../map/viewport';
import { AgvCard, type AgvDetail } from './AgvCard';
import { AppHeader, type SignedInUser } from './AppHeader';
import { ConsumableCard } from './ConsumableCard';
import type { ControlStatus } from './controlStatus';
import { EquipmentCard, type EquipmentDetail } from './EquipmentCard';
import { ListPanel, type ConsumableHit, type ListItemRef } from './ListPanel';
import type { ManualOutcome } from './manual';
import { ManualPanel, type DestinationOption, type ManualStepCommand } from './ManualPanel';
import { MessageRail, MessageTab, type Message } from './MessageRail';
import type { Permissions } from './permissions';
import { RunSwitch } from './RunSwitch';
import type { ScreenKey } from './screens';
import { clickedInside } from './outside';

export interface MapScreenProps {
  readonly layout: MapLayout;
  readonly agvs: readonly AgvDetail[];
  readonly equipment: readonly EquipmentDetail[];
  readonly reservedPanels: readonly PanelId[];
  readonly routes: readonly (readonly PanelId[])[];
  readonly messages: readonly Message[];
  readonly run: RunIndicator;
  /** 運転の切り替えを押せないようにするか（サーバーとつながっていない、送っている途中など）。 */
  readonly runSwitchDisabled: boolean;
  readonly user: SignedInUser;
  readonly permissions: Permissions;
  readonly mapVersion: string;
  /** マニュアルで選べる行き先。 */
  readonly destinations: readonly DestinationOption[];
  /** 消耗品を ID で探す（一覧の消耗品のタブ）。 */
  readonly searchConsumables: (query: string) => readonly ConsumableHit[];
  /** マップを描く大きさ（テストと Storybook のため。なければ、置かれた場所の大きさを測る）。 */
  readonly mapViewportSize?: Size;
  readonly onNavigate: (screen: ScreenKey) => void;
  readonly onChangePassword: () => void;
  readonly onLogout: () => void;
  readonly onStart: () => void;
  readonly onStop: () => void;
  readonly onMarkAllSeen: () => void;
  readonly onEmergencyStop: (id: AgvId) => void;
  readonly onChangeControl: (id: AgvId, status: ControlStatus, needsPosition: boolean) => void;
  readonly onAlarmClear: (id: AgvId) => void;
  readonly onPositionReset: (id: AgvId) => void;
  readonly onManualMove: (id: AgvId, destination: PanelId, arrival: Heading | null) => Promise<ManualOutcome>;
  readonly onManualStep: (id: AgvId, step: ManualStepCommand) => Promise<ManualOutcome>;
  readonly onManualCarrier: (id: AgvId, action: 'raise' | 'lower') => Promise<ManualOutcome>;
  /** 装置やステーションを、詳しい画面で開く。 */
  readonly onOpenEquipment: (screen: ScreenKey, id: EquipmentId) => void;
  /** 消耗品を「消耗品の検索と手入力」の画面で開く。 */
  readonly onOpenConsumable: (id: string) => void;
}

/** 左に出しているもの。一覧か、一覧かマップから選んだもののカード。 */
type Drawer = { readonly kind: 'list' } | (ListItemRef & { readonly fromList: boolean }) | null;

/** 押してもカードを閉じない場所（カード、マップの AGV と装置、マップの上のボタン、メッセージの列）。 */
const KEEP_CARD = '[data-agv-card], [data-agv], [data-equipment], [data-map-controls], [data-keep-card]';

function isNarrow(): boolean {
  return typeof window !== 'undefined' && typeof window.matchMedia === 'function' && window.matchMedia('(max-width: 640px)').matches;
}

/** カードを開いたときに覆われるところ。パソコンでは左のカード（幅 380px）、スマートフォンでは下のカード（画面の 55%）。 */
function cardCovers(): Obstruction {
  if (typeof window === 'undefined') {
    return { left: 0, bottom: 0 };
  }
  return isNarrow() ? { left: 0, bottom: window.innerHeight * 0.55 } : { left: Math.min(380, window.innerWidth - 32), bottom: 0 };
}

/**
 * 1 全体の様子（マップ）。床のいまの様子を見て、その場で対処する画面。
 * サーバーとのやり取りはしない（受け取ったものを描き、操作を知らせる）。
 */
export function MapScreen(props: MapScreenProps) {
  const { layout, agvs, equipment, messages, permissions } = props;
  const [drawer, setDrawer] = useState<Drawer>(null);
  // マップの上か一覧の行でマウスを乗せているもの
  const [hovered, setHovered] = useState<ListItemRef | null>(null);
  const [consumable, setConsumable] = useState<ConsumableHit | null>(null);
  const unreadCount = messages.filter((m) => m.unread).length;
  // null のあいだは、まだ見ていないメッセージがあれば開いておく（スマートフォンでは、たたんでおく）
  const [railChoice, setRailChoice] = useState<boolean | null>(null);
  const railOpen = railChoice ?? (unreadCount > 0 && !isNarrow());

  const agv = drawer?.kind === 'agv' ? (agvs.find((a) => a.id === drawer.id) ?? null) : null;
  const chosenEquipment = drawer?.kind === 'equipment' ? (equipment.find((e) => e.id === drawer.id) ?? null) : null;
  const chosenConsumable = drawer?.kind === 'consumable' ? consumable : null;
  const back = drawer && drawer.kind !== 'list' && drawer.fromList ? () => setDrawer({ kind: 'list' }) : undefined;

  // 消耗品は、ある場所（AGV か、装置かステーション）を、マップで目立たせる
  const located = (ref: ListItemRef | null): ListItemRef | null =>
    ref?.kind === 'consumable' ? (ref.id === consumable?.id ? consumable.at : null) : ref;
  const emphasis = located(hovered) ?? (chosenConsumable ? chosenConsumable.at : null);
  const focusPanel =
    agv?.panel ??
    chosenEquipment?.panel ??
    (chosenConsumable
      ? chosenConsumable.at.kind === 'agv'
        ? agvs.find((a) => a.id === chosenConsumable.at.id)?.panel
        : equipment.find((e) => e.id === chosenConsumable.at.id)?.panel
      : null) ??
    null;

  const close = () => {
    setDrawer(null);
    setHovered(null);
  };

  useEffect(() => {
    if (!drawer) {
      return undefined;
    }
    const closeOutside = (e: MouseEvent) => {
      if (!clickedInside(e, KEEP_CARD)) {
        setDrawer(null);
        setHovered(null);
      }
    };
    document.addEventListener('click', closeOutside);
    return () => document.removeEventListener('click', closeOutside);
  }, [drawer]);

  const listOpen = drawer?.kind === 'list';

  return (
    <div className="flex min-h-screen flex-col bg-ground font-sans text-graphite">
      <AppHeader
        run={props.run}
        user={props.user}
        current="map"
        mapVersion={props.mapVersion}
        onNavigate={props.onNavigate}
        onChangePassword={props.onChangePassword}
        onLogout={props.onLogout}
      />
      <main className="flex min-h-0 flex-1">
        <section aria-label="マップ" className="relative min-h-[420px] min-w-0 flex-1 bg-floor">
          <div className="absolute inset-0">
            <MapViewport
              layout={layout}
              agvs={agvs}
              reservedPanels={props.reservedPanels}
              routes={props.routes}
              selectedAgv={agv?.id ?? null}
              highlightedAgv={emphasis?.kind === 'agv' ? emphasis.id : null}
              onSelectAgv={(id) => setDrawer({ kind: 'agv', id, fromList: false })}
              onHighlightAgv={(id) => setHovered(id ? { kind: 'agv', id } : null)}
              equipment={equipment}
              selectedEquipment={chosenEquipment?.id ?? null}
              highlightedEquipment={emphasis?.kind === 'equipment' ? emphasis.id : null}
              onSelectEquipment={(id) => setDrawer({ kind: 'equipment', id, fromList: false })}
              onHighlightEquipment={(id) => setHovered(id ? { kind: 'equipment', id } : null)}
              focus={focusPanel ? crossPoint(panelOf(layout, focusPanel), layout.panelSizeMm) : null}
              covered={cardCovers()}
              controls={
                <button
                  type="button"
                  aria-label="一覧"
                  title="一覧"
                  aria-pressed={listOpen}
                  onClick={() => (listOpen ? close() : setDrawer({ kind: 'list' }))}
                  className={`flex size-11 items-center justify-center rounded-lg border border-control-border focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-focus ${listOpen ? 'bg-graphite text-white' : 'bg-white text-graphite'}`}
                >
                  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                    <path d="M9 6h11" />
                    <path d="M9 12h11" />
                    <path d="M9 18h11" />
                    <circle cx="4.5" cy="6" r="1.5" />
                    <circle cx="4.5" cy="12" r="1.5" />
                    <circle cx="4.5" cy="18" r="1.5" />
                  </svg>
                </button>
              }
              {...(props.mapViewportSize ? { size: props.mapViewportSize } : {})}
            />
          </div>
          <div data-keep-card="" className="absolute top-3 right-0 max-sm:top-auto max-sm:right-auto max-sm:bottom-0 max-sm:left-1/2 max-sm:-translate-x-1/2">
            <MessageTab open={railOpen} unreadCount={unreadCount} onToggle={() => setRailChoice(!railOpen)} />
          </div>
          <div data-keep-card="" className="absolute right-3 bottom-3 max-sm:bottom-14">
            <RunSwitch run={props.run} disabled={props.runSwitchDisabled} onStart={props.onStart} onStop={props.onStop} />
          </div>
        </section>
        {railOpen && (
          <aside
            id="message-rail"
            data-keep-card=""
            className="w-[300px] shrink-0 overflow-y-auto bg-paper border-l border-line max-sm:fixed max-sm:inset-x-0 max-sm:bottom-0 max-sm:z-24 max-sm:h-[55vh] max-sm:w-auto max-sm:animate-sheet-in max-sm:rounded-t-2xl max-sm:border-t max-sm:border-l-0 max-sm:shadow-[0_-6px_20px_rgba(35,38,43,0.18)]"
          >
            <MessageRail messages={messages} onMarkAllSeen={props.onMarkAllSeen} onClose={() => setRailChoice(false)} />
          </aside>
        )}
      </main>
      {drawer && (
        <div
          data-agv-card=""
          className="fixed inset-y-0 left-0 z-25 w-[380px] max-w-[calc(100vw-32px)] animate-drawer-in overflow-y-auto border-r border-line bg-paper shadow-[6px_0_20px_rgba(35,38,43,0.18)] motion-reduce:animate-none max-sm:top-auto max-sm:right-0 max-sm:h-[55vh] max-sm:w-auto max-sm:max-w-none max-sm:animate-sheet-in max-sm:rounded-t-2xl max-sm:border-t max-sm:border-r-0"
        >
          {drawer.kind === 'list' ? (
            <ListPanel
              agvs={agvs}
              equipment={equipment}
              searchConsumables={props.searchConsumables}
              isChargingPanel={(panel) => panelOf(layout, panel).hasChargingPin}
              onSelect={(item) => {
                if (item.kind === 'consumable') {
                  setConsumable(props.searchConsumables(item.id).find((c) => c.id === item.id) ?? null);
                }
                setHovered(null);
                setDrawer({ ...item, fromList: true });
              }}
              onHighlight={(item) => {
                if (item?.kind === 'consumable') {
                  setConsumable(props.searchConsumables(item.id).find((c) => c.id === item.id) ?? null);
                }
                setHovered(item);
              }}
              onClose={close}
            />
          ) : agv ? (
            <AgvCard
              agv={agv}
              onChargingPin={panelOf(layout, agv.panel).hasChargingPin}
              permissions={permissions}
              onClose={close}
              onBack={back}
              onEmergencyStop={props.onEmergencyStop}
              onChangeControl={props.onChangeControl}
              onAlarmClear={props.onAlarmClear}
              onPositionReset={props.onPositionReset}
              manual={
                <ManualPanel
                  input={{ operation: agv.operation, activity: agv.manualActivity ?? 'none', heading: agv.heading }}
                  destinations={props.destinations}
                  onMove={(destination, arrival) => props.onManualMove(agv.id, destination, arrival)}
                  onStep={(step) => props.onManualStep(agv.id, step)}
                  onCarrier={(action) => props.onManualCarrier(agv.id, action)}
                />
              }
            />
          ) : chosenEquipment ? (
            <EquipmentCard equipment={chosenEquipment} permissions={permissions} onClose={close} onBack={back} onOpen={props.onOpenEquipment} />
          ) : chosenConsumable ? (
            <ConsumableCard consumable={chosenConsumable} onClose={close} onBack={back} onOpen={props.onOpenConsumable} />
          ) : null}
        </div>
      )}
    </div>
  );
}
