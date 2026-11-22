import { useEffect, useState } from 'react';
import type { AgvId } from '../map/agv';
import { crossPoint, panelOf } from '../map/drawing';
import { MapViewport } from '../map/MapViewport';
import type { Heading, MapLayout, PanelId } from '../map/model';
import type { Obstruction, Size } from '../map/viewport';
import type { EquipmentId } from '../map/equipment';
import { AgvCard, type AgvDetail } from './AgvCard';
import { EquipmentCard, type EquipmentDetail } from './EquipmentCard';
import { AppHeader, type SignedInUser } from './AppHeader';
import type { ControlStatus } from './controlStatus';
import type { ManualOutcome } from './manual';
import { ManualPanel, type DestinationOption, type ManualStepCommand } from './ManualPanel';
import { MessageRail, MessageTab, type Message } from './MessageRail';
import type { Permissions } from './permissions';
import { RunSwitch } from './RunSwitch';
import type { ScreenKey } from './screens';

export interface MapScreenProps {
  readonly layout: MapLayout;
  readonly agvs: readonly AgvDetail[];
  readonly equipment: readonly EquipmentDetail[];
  readonly reservedPanels: readonly PanelId[];
  readonly routes: readonly (readonly PanelId[])[];
  readonly messages: readonly Message[];
  readonly running: boolean;
  readonly user: SignedInUser;
  readonly permissions: Permissions;
  readonly mapVersion: string;
  /** マニュアルで選べる行き先。 */
  readonly destinations: readonly DestinationOption[];
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
}

/** カードを開いているもの。AGV か、装置かステーション。 */
type Selection = { readonly kind: 'agv'; readonly id: AgvId } | { readonly kind: 'equipment'; readonly id: EquipmentId } | null;

/** 押してもカードを閉じない場所（カード、マップの AGV、マップの上のボタン、メッセージの列）。 */
const KEEP_CARD = '[data-agv-card], [data-agv], [data-equipment], [data-keep-card]';

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
  const [selection, setSelection] = useState<Selection>(null);
  const [highlighted, setHighlighted] = useState<AgvId | null>(null);
  const [highlightedEquipment, setHighlightedEquipment] = useState<EquipmentId | null>(null);
  const selected = selection?.kind === 'agv' ? selection.id : null;
  const selectedEquipment = selection?.kind === 'equipment' ? selection.id : null;
  const unreadCount = messages.filter((m) => m.unread).length;
  // null のあいだは、まだ見ていないメッセージがあれば開いておく（スマートフォンでは、たたんでおく）
  const [railChoice, setRailChoice] = useState<boolean | null>(null);
  const railOpen = railChoice ?? (unreadCount > 0 && !isNarrow());
  const agv = agvs.find((a) => a.id === selected) ?? null;
  const chosenEquipment = equipment.find((e) => e.id === selectedEquipment) ?? null;
  const focusPanel = agv?.panel ?? chosenEquipment?.panel ?? null;

  useEffect(() => {
    if (!selection) {
      return undefined;
    }
    const closeOutside = (e: MouseEvent) => {
      if (e.target instanceof Element && !e.target.closest(KEEP_CARD)) {
        setSelection(null);
      }
    };
    document.addEventListener('click', closeOutside);
    return () => document.removeEventListener('click', closeOutside);
  }, [selection]);

  return (
    <div className="flex min-h-screen flex-col bg-ground font-sans text-graphite">
      <AppHeader
        running={props.running}
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
              selectedAgv={selected}
              highlightedAgv={highlighted}
              onSelectAgv={(id) => setSelection({ kind: 'agv', id })}
              onHighlightAgv={setHighlighted}
              equipment={equipment}
              selectedEquipment={selectedEquipment}
              highlightedEquipment={highlightedEquipment}
              onSelectEquipment={(id) => setSelection({ kind: 'equipment', id })}
              onHighlightEquipment={setHighlightedEquipment}
              focus={focusPanel ? crossPoint(panelOf(layout, focusPanel), layout.panelSizeMm) : null}
              covered={cardCovers()}
              {...(props.mapViewportSize ? { size: props.mapViewportSize } : {})}
            />
          </div>
          <div data-keep-card="" className="absolute top-3 right-0 max-sm:top-auto max-sm:right-auto max-sm:bottom-0 max-sm:left-1/2 max-sm:-translate-x-1/2">
            <MessageTab open={railOpen} unreadCount={unreadCount} onToggle={() => setRailChoice(!railOpen)} />
          </div>
          <div data-keep-card="" className="absolute right-3 bottom-3 max-sm:bottom-14">
            <RunSwitch running={props.running} onStart={props.onStart} onStop={props.onStop} />
          </div>
        </section>
        {railOpen && (
          <aside
            id="message-rail"
            data-keep-card=""
            className="w-[300px] shrink-0 overflow-y-auto border-l border-line max-sm:fixed max-sm:inset-x-0 max-sm:bottom-0 max-sm:z-24 max-sm:h-[55vh] max-sm:w-auto max-sm:animate-sheet-in max-sm:rounded-t-2xl max-sm:border-t max-sm:border-l-0 max-sm:shadow-[0_-6px_20px_rgba(35,38,43,0.18)]"
          >
            <MessageRail messages={messages} onMarkAllSeen={props.onMarkAllSeen} onClose={() => setRailChoice(false)} />
          </aside>
        )}
      </main>
      {(agv || chosenEquipment) && (
        <div
          data-agv-card=""
          className="fixed inset-y-0 left-0 z-25 w-[380px] max-w-[calc(100vw-32px)] animate-drawer-in overflow-y-auto border-r border-line bg-paper shadow-[6px_0_20px_rgba(35,38,43,0.18)] motion-reduce:animate-none max-sm:top-auto max-sm:right-0 max-sm:h-[55vh] max-sm:w-auto max-sm:max-w-none max-sm:animate-sheet-in max-sm:rounded-t-2xl max-sm:border-t max-sm:border-r-0"
        >
          {agv ? (
          <AgvCard
            agv={agv}
            onChargingPin={panelOf(layout, agv.panel).hasChargingPin}
            permissions={permissions}
            onClose={() => setSelection(null)}
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
            <EquipmentCard
              equipment={chosenEquipment}
              permissions={permissions}
              onClose={() => setSelection(null)}
              onOpen={props.onOpenEquipment}
            />
          ) : null}
        </div>
      )}
    </div>
  );
}
