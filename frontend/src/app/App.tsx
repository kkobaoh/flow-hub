import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { FleetPage } from '../pages/FleetPage';
import { ConnectionBanner } from './layouts/ConnectionBanner';
import { ToolSettingsPage } from '../pages/ToolSettingsPage';
import { ConsumablesPage } from '../pages/ConsumablesPage';
import { ConfirmDialog } from '../shared/components/ConfirmDialog';
import { DevicesPage } from '../pages/DevicesPage';
import { matchesQuery } from '../shared/utils/listing';
import { MapEditPage } from '../pages/MapEditPage';
import { MapPage } from '../pages/MapPage';
import { useRoute, type Route } from './router';
import { initialRoles } from '../features/auth/permissions';
import { MainLayout } from './layouts/MainLayout';
import { screens, type ScreenKey } from './screens';
import { StationPage } from '../pages/StationPage';
import { DispatchPage } from '../pages/DispatchPage';
import { fixProblem } from '../features/tool/station';
import { UserDirectoryPage } from '../pages/UserDirectoryPage';
import type { AgvId } from '../features/fleet/agv';
import { panelOptions } from '../features/fleet/panelOptions';
import { latestSequence, railMessages, readSeenUpTo, writeSeenUpTo, type LocalMessage } from '../features/alarm/messageList';
import type { CommandResult } from '../shared/api/postCommand';
import type { ServerLink } from './realtime/link';
import { commandNotice, toManualOutcome, type AgvCommand, type AgvCommandResult } from '../features/fleet/api/agvCommands';
import { mapOverlays, toAgvDetails } from '../features/fleet/api/agvSnapshots';
import { runIndicator } from '../features/system/systemStatus';
import { useServer } from './realtime/useServer';
import type { MapLayout } from '../features/routing/model';
import type { MapSnapshot } from '../features/routing/api/mapSnapshot';
import { equipmentDetails } from '../features/tool/equipmentDetails';
import { deviceInsideOf, stationInventoryOf } from '../features/tool/inside';
import { toTransports } from '../features/dispatch/api/transportsSnapshot';
import { toHit, toRecord } from '../features/tool/api/consumablesSnapshot';
import { agvRegistrationNotice, agvTypeNotice, type AgvRegistrationCommand } from '../features/fleet/api/fleetSnapshot';
import { configurationNotice, type ConfigurationCommand } from '../features/tool/api/configurationSnapshot';
import type { ProcessKind } from '../features/tool/config';
import { carrierHeightNotice, mapDraftNotice, mapPublishNotice, type MapDraftLoad } from '../features/routing/api/mapDraft';
import { cancelNotice } from '../features/dispatch/api/transportCommands';
import { consumableNotice, type ConsumableCommand } from '../features/tool/api/consumableCommands';

const user = { name: '[ユーザー名]', role: '管理者' } as const;
const permissions = initialRoles.管理者;

/** マップが届くまで、またはまだ公開されていないときのマップ（パネルがない）。 */
const EMPTY_LAYOUT: MapLayout = { panelSizeMm: 1000, panels: [], connections: [] };

/** メニューに出す、公開中のマップの版。 */
function mapVersionLabel(map: MapSnapshot | null): string {
  if (map === null) {
    return 'マップ（読み込んでいます）';
  }
  return map.mapVersion > 0 ? `マップ 版${map.mapVersion}（公開済み）` : 'マップ（まだ公開していません）';
}

/** ブラウザーの置き場（使えないときは null）。 */
function browserStorage(): Storage | null {
  try {
    return window.localStorage;
  } catch {
    return null;
  }
}


/** 運転の切り替えを送れなかったときに出すメッセージ。 */
const commandFailed = {
  start: { title: 'スタートできたかわかりません', detail: 'サーバーから応答がなかったか、受け付けられませんでした。表示の運転状態を確かめてください。' },
  stop: { title: '停止できたかわかりません', detail: 'サーバーから応答がなかったか、受け付けられませんでした。表示の運転状態を確かめてください。' },
} as const;

/** サーバーとつないだデータで出す画面（ほかの画面には、見本のデータだと帯で知らせる）。 */
const SERVER_SCREENS: ReadonlySet<ScreenKey> = new Set(['map', 'transports', 'devices', 'station', 'consumables', 'agvs', 'consumableConfig', 'users']);


/** キャリアの種類の候補（いまある加工の種類のもの。重ねず、名前の順）。 */
const carrierSuggestions = (kinds: readonly ProcessKind[]) => [...new Set(kinds.map((k) => k.carrier))].sort();

/** サーバーから様子が届くまで出す。 */
function Loading({ what }: { readonly what: string }) {
  return <p className="m-0 rounded-[10px] border border-line bg-paper p-5 text-[15px] text-ink-muted">{what}を読み込んでいます。</p>;
}

export interface AppProps {
  /** サーバーとのつながり（本物は main.tsx で作る。テストと Storybook は偽物）。 */
  readonly link: ServerLink;
}

/**
 * 画面の入り口。マップ、運転状態、AGV、メッセージ、搬送の一覧、装置とストッカー、ステーションの在庫、消耗品の検索はサーバーとつないで、届いた様子を出す。ほかは、まだ見本のデータで画面を動かす。
 * 見本のデータを操作した結果は、この中で覚えて、画面が切り替わるようにしている。
 */
export function App({ link }: AppProps) {
  const [route, navigate] = useRoute();
  const server = useServer(link);
  const run = runIndicator(server.status);
  const [sending, setSending] = useState(false);
  // メッセージは、サーバーに残しているものと、その画面だけの知らせ（操作が届かなかった、など）を混ぜて出す
  const [localMessages, setLocalMessages] = useState<readonly LocalMessage[]>([]);
  const messageCount = useRef(0);
  // どこまで見たか（メッセージの番号）は、ログインができるまではブラウザーごとに覚える
  const [seenUpTo, setSeenUpTo] = useState(() => readSeenUpTo(browserStorage()));
  const serverMessages = server.messages.kind === 'waiting' ? [] : server.messages.snapshot.messages;
  const messages = railMessages(serverMessages, seenUpTo, localMessages, new Date());
  const markAllSeen = () => {
    const latest = Math.max(seenUpTo, latestSequence(serverMessages));
    writeSeenUpTo(browserStorage(), latest);
    setSeenUpTo(latest);
    setLocalMessages((ms) => ms.map((m) => ({ ...m, unread: false })));
  };
  // AGV ごとの、送って結果を待っている操作の数と、受け付けられなかったときの知らせ
  const [sendingAgvs, setSendingAgvs] = useState<ReadonlyMap<AgvId, number>>(new Map());
  const [agvNotices, setAgvNotices] = useState<ReadonlyMap<AgvId, string>>(new Map());
  const offline = server.connection !== 'connected';
  // マップの編集の画面は、開くたびにサーバーから下書きを読む。読めるまでは、つながっていれば読み直す。
  // 読んだあとは、「サーバーから読み直す」を押したときだけ読み直す（つながり直しても、画面の変更は捨てない）
  const [draftView, setDraftView] = useState<{ readonly kind: 'loading' } | { readonly kind: 'failed' } | { readonly kind: 'loaded'; readonly load: MapDraftLoad; readonly key: number }>({
    kind: 'loading',
  });
  const reloadDraft = useCallback(async () => {
    const result = await link.loadMapDraft();
    setDraftView((view) => (result === 'failed' ? { kind: 'failed' } : { kind: 'loaded', load: result, key: (view.kind === 'loaded' ? view.key : 0) + 1 }));
  }, [link]);
  const onMapEdit = route.screen === 'mapEdit';
  useEffect(() => {
    if (!onMapEdit) {
      setDraftView({ kind: 'loading' }); // 次に開いたときに読み直す
    } else if (!offline && draftView.kind === 'loading') {
      void reloadDraft();
    }
  }, [onMapEdit, offline, draftView.kind, reloadDraft]);
  // マップの画面は、サーバーから届いた公開中のマップを描く（届くまでは、まだない）
  const serverMap = server.map.kind === 'waiting' ? null : server.map.snapshot;
  const layout = serverMap?.layout ?? EMPTY_LAYOUT;
  // 消耗品の構成で選べる受け渡しパネル（公開中のマップの受け渡しパネルと特殊消耗品受け渡しパネル）
  const transferPanels = useMemo(() => layout.panels.filter((p) => p.type === 'Transfer' || p.type === 'SpecialTransfer').map((p): string => p.id), [layout]);
  // マップの編集の画面の装置とステーション（消耗品の構成から。届くまでは、ないものとする）
  const editEquipment = useMemo(
    () => (server.configuration.kind === 'waiting' ? [] : [...server.configuration.snapshot.devices, ...server.configuration.snapshot.stations]),
    [server.configuration],
  );
  const destinations = useMemo(() => panelOptions(layout), [layout]);
  const equipmentStatus = server.equipment.kind === 'waiting' ? null : server.equipment.snapshot;
  const equipment = useMemo(() => equipmentDetails(serverMap?.equipment ?? [], equipmentStatus), [serverMap, equipmentStatus]);
  // 装置とストッカー、ステーションの在庫の画面も、届いた装置とステーションの様子から出す
  const deviceInsides = useMemo(() => equipmentStatus?.devices.map(deviceInsideOf) ?? [], [equipmentStatus]);
  const stationInventories = useMemo(() => equipmentStatus?.stations.map(stationInventoryOf) ?? [], [equipmentStatus]);
  /** 消耗品の操作を送り、断られたら理由の文を返す（やり直さない。表示は、届いた様子で変える）。 */
  // 消耗品の検索と、マップの「消耗品を探す」も、届いた消耗品の記録から出す
  const consumableData = useMemo(() => (server.consumables.kind === 'waiting' ? [] : server.consumables.snapshot.consumables), [server.consumables]);
  const records = useMemo(() => consumableData.map(toRecord), [consumableData]);
  const hits = useMemo(() => consumableData.map(toHit), [consumableData]);
  // 搬送の一覧も、届いた様子から出す
  const transportList = useMemo(() => (server.transports.kind === 'waiting' ? [] : toTransports(server.transports.snapshot.transports)), [server.transports]);
  /** AGV の登録の操作を送り、断られたら理由の文を返す（やり直さない。表示は、届いた様子で変える）。 */
  const operateAgvRegistrations = async (command: AgvRegistrationCommand) => agvRegistrationNotice(await link.operateAgvRegistrations(command));
  const operateConsumables = async (command: ConsumableCommand) => consumableNotice(command.kind, await link.operateConsumables(command));
  /** 消耗品の構成の操作を送り、断られたら理由の文を返す（やり直さない。表示は、届いた様子で変える）。 */
  const operateConfiguration = async (command: ConfigurationCommand) => configurationNotice(command, await link.operateConfiguration(command));
  // AGV は、サーバーから届いた様子を、描いているマップに合わせて使う（まだ届いていなければ、いない）
  const agvs = useMemo(
    () => (server.agvs.kind === 'waiting' ? [] : toAgvDetails(server.agvs.snapshot.agvs, layout)),
    [server.agvs, layout],
  );
  // 予約しているパネルと走る予定の経路も、サーバーから届いた AGV の様子から描く
  const overlays = useMemo(
    () => (server.agvs.kind === 'waiting' ? { reservedPanels: [], routes: [] } : mapOverlays(server.agvs.snapshot.agvs, layout)),
    [server.agvs, layout],
  );
  const [mapDirty, setMapDirty] = useState(false);
  const [leaving, setLeaving] = useState<{ readonly screen: ScreenKey; readonly target: string | undefined } | null>(null);
  const go = (screen: ScreenKey, target?: string) => {
    if (route.screen === 'mapEdit' && mapDirty && screen !== 'mapEdit') {
      setLeaving({ screen, target });
      return;
    }
    navigate(screen, target);
  };
  /** AGV へ操作を送る。結果が返るまでは、その AGV の操作を押せなくする（緊急停止のほかは）。 */
  const operate = async (id: AgvId, command: AgvCommand): Promise<AgvCommandResult> => {
    const count = (delta: number) =>
      setSendingAgvs((m) => {
        const next = new Map(m);
        const n = (next.get(id) ?? 0) + delta;
        if (n > 0) {
          next.set(id, n);
        } else {
          next.delete(id);
        }
        return next;
      });
    count(1);
    setAgvNotices((m) => {
      const next = new Map(m);
      next.delete(id);
      return next;
    });
    try {
      return await link.operateAgv(id, command);
    } finally {
      count(-1);
    }
  };
  /** AGV へ操作を送り、受け付けられなかったら、カードの中に知らせる（やり直さない）。 */
  const operateWithNotice = (id: AgvId, command: AgvCommand) => {
    void operate(id, command).then((result) => {
      const notice = commandNotice(result);
      if (notice !== null) {
        setAgvNotices((m) => new Map(m).set(id, notice));
      }
    });
  };

  /** 運転の切り替えを送る。表示は、サーバーから届いた様子で変える。送れなかったら知らせる（やり直さない）。 */
  const sendRun = async (kind: 'start' | 'stop') => {
    setSending(true);
    let result: CommandResult;
    try {
      result = await (kind === 'start' ? link.startSystem() : link.stopSystem());
    } finally {
      setSending(false);
    }
    if (result === 'failed') {
      messageCount.current += 1;
      const id = `run-${messageCount.current}`;
      setLocalMessages((ms) => [...ms, { id, at: new Date(), ...commandFailed[kind], unread: true }]);
    }
  };
  const frame = {
    run,
    user,
    permissions,
    mapVersion: mapVersionLabel(serverMap),
    messages,
    onMarkAllSeen: markAllSeen,
    onNavigate: go,
    onChangePassword: () => {},
    onLogout: () => {},
  };

  const content = (r: Route): ReactNode => {
    switch (r.screen) {
      case 'transports':
        return server.transports.kind === 'waiting' ? (
          <Loading what="搬送の一覧" />
        ) : (
          <DispatchPage
            transports={transportList}
            permissions={permissions}
            offline={offline}
            onCancel={async (n) => cancelNotice(await link.cancelTransport(n))}
          />
        );
      case 'devices':
        return equipmentStatus === null ? (
          <Loading what="装置とステーションの様子" />
        ) : (
          <DevicesPage
            devices={deviceInsides}
            selected={r.target}
            varieties={equipmentStatus.varieties}
            permissions={permissions}
            offline={offline}
            onSelect={(id) => navigate('devices', id)}
            onSaveSetting={(id, rows) => operateConsumables({ kind: 'stockerSetting', device: id, counts: rows.map((row) => ({ variety: row.variety, count: row.target })) })}
          />
        );
      case 'station':
        return equipmentStatus === null ? (
          <Loading what="装置とステーションの様子" />
        ) : (
          <StationPage
            stations={stationInventories}
            selected={r.target}
            varieties={equipmentStatus.varieties}
            permissions={permissions}
            offline={offline}
            onSelect={(id) => navigate('station', id)}
            onShip={(_, item) => operateConsumables({ kind: 'ship', consumable: item })}
            onFix={(_, item, fix) =>
              fix.condition === null
                ? Promise.resolve(fixProblem(fix))
                : operateConsumables({ kind: 'correct', consumable: item, variety: fix.variety, condition: fix.condition })
            }
          />
        );
      case 'consumables':
        return server.consumables.kind === 'waiting' || equipmentStatus === null ? (
          <Loading what="消耗品の記録" />
        ) : (
          <ConsumablesPage
            key={r.target ?? ''}
            records={records}
            selected={r.target}
            stations={stationInventories.map((s) => s.id)}
            devices={deviceInsides}
            varieties={equipmentStatus.varieties}
            permissions={permissions}
            offline={offline}
            onOpen={(id) => navigate('consumables', id)}
            onNominate={(id, device) => operateConsumables({ kind: 'designate', consumable: id, device })}
            onCancelNomination={(id) => operateConsumables({ kind: 'cancelDesignation', consumable: id })}
            onRegister={(record) => operateConsumables({ kind: 'register', record })}
            onEdit={(record) => operateConsumables({ kind: 'edit', record })}
            onDelete={async (id) => {
              const notice = await operateConsumables({ kind: 'delete', consumable: id });
              if (notice === null) {
                navigate('consumables'); // 消したので、開いていた消耗品を閉じる
              }
              return notice;
            }}
          />
        );
      case 'agvs':
        return server.fleet.kind === 'waiting' ? (
          <Loading what="AGV と AGV の種類の登録" />
        ) : (
          <FleetPage
            entries={server.fleet.snapshot.entries}
            types={server.fleet.snapshot.types}
            carriers={server.fleet.snapshot.carriers}
            defaults={server.fleet.snapshot.defaults}
            permissions={permissions}
            offline={offline}
            onAddAgv={(draft) => operateAgvRegistrations({ kind: 'add', id: draft.id, type: draft.type, carrier: draft.carrier })}
            onChangeAgv={(id, change) => operateAgvRegistrations({ kind: 'change', id, ...change })}
            onAlignAgv={(id, change) =>
              operateAgvRegistrations({ kind: 'align', id, type: change.type === '' ? null : change.type, carrier: change.carrier === '' ? null : change.carrier })
            }
            onDeleteAgv={(id) => operateAgvRegistrations({ kind: 'delete', id })}
            onSaveType={async (original, type) => agvTypeNotice(await link.operateAgvTypes({ kind: original === null ? 'add' : 'edit', type }))}
            onDeleteType={async (id) => agvTypeNotice(await link.operateAgvTypes({ kind: 'delete', id }))}
          />
        );
      case 'consumableConfig':
        return server.configuration.kind === 'waiting' ? (
          <Loading what="消耗品の構成" />
        ) : (
          <ToolSettingsPage
            processKinds={server.configuration.snapshot.processKinds}
            varieties={server.configuration.snapshot.varieties}
            devices={server.configuration.snapshot.devices}
            stations={server.configuration.snapshot.stations}
            carriers={carrierSuggestions(server.configuration.snapshot.processKinds)}
            transferPanels={transferPanels}
            permissions={permissions}
            offline={offline}
            onSaveProcessKind={(original, k) => operateConfiguration({ kind: original === null ? 'addProcessKind' : 'editProcessKind', processKind: k })}
            onDeleteProcessKind={(name) => operateConfiguration({ kind: 'deleteProcessKind', name })}
            onSaveVariety={(original, v) => operateConfiguration({ kind: original === null ? 'addVariety' : 'editVariety', name: v.name, processKind: v.processKind })}
            onDeleteVariety={(name) => operateConfiguration({ kind: 'deleteVariety', name })}
            onAddId={(variety, id) => operateConfiguration({ kind: 'addId', variety, id })}
            onRemoveId={(_, id) => operateConfiguration({ kind: 'removeId', id })}
            onSaveDevice={(original, device) => operateConfiguration({ kind: original === null ? 'addDevice' : 'editDevice', device })}
            onDeleteDevice={(id) => operateConfiguration({ kind: 'deleteDevice', id })}
            onSaveStation={(original, station) => operateConfiguration({ kind: original === null ? 'addStation' : 'editStation', station })}
            onDeleteStation={(id) => operateConfiguration({ kind: 'deleteStation', id })}
          />
        );
      case 'users':
        return <UserDirectoryPage />;
      case 'mapEdit':
        if (draftView.kind === 'failed') {
          return (
            <div className="flex flex-col items-start gap-3 rounded-[10px] border border-line bg-paper p-5 text-[15px]">
              <p className="m-0">マップの下書きを読めませんでした。サーバーから応答がありません。</p>
              <button type="button" onClick={() => void reloadDraft()} className="min-h-11 rounded-md border-2 border-graphite px-4 font-bold">
                読み直す
              </button>
            </div>
          );
        }
        if (draftView.kind === 'loading') {
          return <Loading what="マップの下書き" />;
        }
        if (draftView.load.draft === null) {
          return <p className="m-0 rounded-[10px] border border-line bg-paper p-5 text-[15px] text-ink-muted">公開中のマップも下書きもありません。</p>;
        }
        return (
          <MapEditPage
            key={draftView.key}
            saved={draftView.load.draft}
            draftVersion={draftView.load.publishedVersion + 1}
            savedAt={draftView.load.savedAt}
            publishedVersion={draftView.load.publishedVersion}
            equipment={editEquipment}
            heights={Object.fromEntries(layout.panels.filter((p) => p.type === 'Transfer').map((p) => [p.id, draftView.load.heights[p.id] ?? null]))}
            running={run !== 'stopped'} // わからないときは、運転中として扱う（公開の決まりが厳しい側）
            travelingAgvs={agvs.filter((a) => a.operation === 'traveling').map((a) => a.id)}
            permissions={permissions}
            offline={offline}
            onSaveDraft={async (d) => {
              const load = draftView.load;
              const outcome = await link.saveMapDraft(load.revision, d);
              if (outcome.result === 'saved') {
                // 画面はそのまま（選んでいるパネルなどを残す）。番号と保存した日時だけ進める
                setDraftView((view) => (view.kind === 'loaded' ? { ...view, load: { ...view.load, revision: outcome.revision, savedAt: new Date(), draft: d } } : view));
              }
              return mapDraftNotice(outcome);
            }}
            onReload={() => void reloadDraft()}
            onValidate={(d) => link.validateMapDraft(d)}
            onPublish={async (d) => {
              const outcome = await link.publishMapDraft(draftView.load.revision, d);
              if (outcome.result === 'published') {
                // 下書きは消えたので、公開した版の写しから始め直す（切り離しにした AGV は、画面のメッセージで知らせる）
                await reloadDraft();
              }
              return mapPublishNotice(outcome);
            }}
            onSaveHeight={async (panel, mm) => {
              const outcome = await link.saveCarrierHeight(panel, mm);
              if (outcome.result === 'saved') {
                setDraftView((view) => {
                  if (view.kind !== 'loaded') {
                    return view;
                  }
                  const { [panel]: _old, ...rest } = view.load.heights;
                  return { ...view, load: { ...view.load, heights: outcome.height === null ? rest : { ...rest, [panel]: outcome.height } } };
                });
              }
              return carrierHeightNotice(panel, outcome);
            }}
            onDirtyChange={setMapDirty}
          />
        );
      default:
        return <p className="m-0 rounded-[10px] border border-line bg-paper p-5 text-[15px] text-ink-muted">この画面は、これから作ります。</p>;
    }
  };

  return (
    <>
      <ConnectionBanner connection={server.connection} />
      {!SERVER_SCREENS.has(route.screen) && <p className="m-0 bg-estop-plate px-3 py-1 text-center text-xs font-bold text-graphite">この画面は、見本のデータです</p>}
      {route.screen === 'map' && layout.panels.length === 0 ? (
        <MainLayout {...frame} current="map" title="全体の様子（マップ）">
          <p className="m-0 rounded-[10px] border border-line bg-paper p-5 text-[15px] text-ink-muted">
            {serverMap === null ? 'マップを読み込んでいます。' : 'マップがまだ公開されていません。マップの編集の画面で公開してください。'}
          </p>
        </MainLayout>
      ) : route.screen === 'map' ? (
        <MapPage
          {...frame}
          layout={layout}
          agvs={agvs}
          equipment={equipment}
          reservedPanels={overlays.reservedPanels}
          routes={overlays.routes}
          destinations={destinations}
          searchConsumables={(query) => hits.filter((c) => matchesQuery(c.id, query))}
          onOpenEquipment={(screen, id) => navigate(screen, id)}
          onOpenConsumable={(id) => navigate('consumables', id)}
          runSwitchDisabled={server.connection !== 'connected' || sending}
          onStart={() => void sendRun('start')}
          onStop={() => void sendRun('stop')}
          offline={offline}
          agvSending={(id) => (sendingAgvs.get(id) ?? 0) > 0}
          agvNotice={(id) => agvNotices.get(id) ?? null}
          onEmergencyStop={(id) => operateWithNotice(id, { kind: 'emergencyStop' })}
          onChangeControl={(id, status, position) => operateWithNotice(id, { kind: 'control', status, position })}
          onAlarmClear={(id) => operateWithNotice(id, { kind: 'alarmClear' })}
          onPositionReset={(id, position) => operateWithNotice(id, { kind: 'positionReset', position })}
          onManualMove={(id, destination, arrival) => operate(id, { kind: 'manualMove', destination, arrival }).then(toManualOutcome)}
          onManualStep={(id, step) => operate(id, { kind: 'manualStep', step }).then(toManualOutcome)}
          onManualCarrier={(id, action) => operate(id, { kind: 'manualCarrier', action }).then(toManualOutcome)}
        />
      ) : (
        <MainLayout {...frame} current={route.screen} title={screens.find((s) => s.key === route.screen)?.title ?? ''}>
          {content(route)}
        </MainLayout>
      )}
      {leaving !== null && (
        <ConfirmDialog
          title="保存していない変更を捨てて移りますか？"
          body="マップの編集で、下書きに保存していない変更があります。移ると、その変更は消えます。"
          confirmLabel="変更を捨てて移る"
          tone="danger"
          onConfirm={() => {
            setMapDirty(false);
            setLeaving(null);
            navigate(leaving.screen, leaving.target);
          }}
          onCancel={() => setLeaving(null)}
        />
      )}
    </>
  );
}
