import { useMemo, useRef, useState, type ReactNode } from 'react';
import {
  demoAgvEntries,
  demoAgvTypes,
  demoCarriers,
  demoConfigVarieties,
  demoDeviceConfigs,
  demoHeights,
  demoMapDraft,
  demoProcessKinds,
  demoRoles,
  demoStationConfigs,
  demoUsers,
  demoValidate,
} from './demo/demo';
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
import { UsersPage } from '../pages/UsersPage';
import { agvId, type AgvId } from '../features/fleet/agv';
import { panelOptions } from '../features/fleet/panelOptions';
import { latestSequence, railMessages, readSeenUpTo, writeSeenUpTo, type LocalMessage } from '../features/alarm/messageList';
import type { CommandResult } from '../shared/api/postCommand';
import type { ServerLink } from './realtime/link';
import { commandNotice, toManualOutcome, type AgvCommand, type AgvCommandResult } from '../features/fleet/api/agvCommands';
import { mapOverlays, toAgvDetails } from '../features/fleet/api/agvSnapshots';
import { runIndicator } from '../features/system/systemStatus';
import { useServer } from './realtime/useServer';
import type { MapLayout } from '../features/routing/model';
import { sampleLayout } from './demo/sampleLayout';
import type { MapSnapshot } from '../features/routing/api/mapSnapshot';
import { equipmentDetails } from '../features/tool/equipmentDetails';
import { deviceInsideOf, stationInventoryOf } from '../features/tool/inside';
import { toTransports } from '../features/dispatch/api/transportsSnapshot';
import { toHit, toRecord } from '../features/tool/api/consumablesSnapshot';
import { cancelNotice } from '../features/dispatch/api/transportCommands';
import { consumableNotice, type ConsumableCommand } from '../features/tool/api/consumableCommands';

const user = { name: '[ユーザー名]', role: '管理者' } as const;
const permissions = initialRoles.管理者;

/** 見本の画面で見せる仮のパスワード（本物は、サーバーが作る）。 */
function demoPassword(): string {
  const letters = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789';
  return Array.from(crypto.getRandomValues(new Uint32Array(12)), (n) => letters[n % letters.length]).join('');
}
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

const transferPanels = sampleLayout.panels.filter((p) => p.type === 'Transfer' || p.type === 'SpecialTransfer').map((p): string => p.id);

/** 運転の切り替えを送れなかったときに出すメッセージ。 */
const commandFailed = {
  start: { title: 'スタートできたかわかりません', detail: 'サーバーから応答がなかったか、受け付けられませんでした。表示の運転状態を確かめてください。' },
  stop: { title: '停止できたかわかりません', detail: 'サーバーから応答がなかったか、受け付けられませんでした。表示の運転状態を確かめてください。' },
} as const;

/** サーバーとつないだデータで出す画面（ほかの画面には、見本のデータだと帯で知らせる）。 */
const SERVER_SCREENS: ReadonlySet<ScreenKey> = new Set(['map', 'transports', 'devices', 'station', 'consumables']);

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
  const [agvEntries, setAgvEntries] = useState(demoAgvEntries);
  const [agvTypes, setAgvTypes] = useState(demoAgvTypes);
  const [processKinds, setProcessKinds] = useState(demoProcessKinds);
  const [configVarieties, setConfigVarieties] = useState(demoConfigVarieties);
  const [deviceConfigs, setDeviceConfigs] = useState(demoDeviceConfigs);
  const [stationConfigs, setStationConfigs] = useState(demoStationConfigs);
  const [users, setUsers] = useState(demoUsers);
  const [roles, setRoles] = useState(demoRoles);
  const [mapDraft, setMapDraft] = useState(demoMapDraft);
  const [draftSavedAt, setDraftSavedAt] = useState<Date | null>(null);
  // マップの編集の画面は、まだ見本の下書きで動かす（画面からの公開は backlog #5）
  const [demoPublished, setDemoPublished] = useState({ version: 3, layout: sampleLayout });
  // マップの画面は、サーバーから届いた公開中のマップを描く（届くまでは、まだない）
  const serverMap = server.map.kind === 'waiting' ? null : server.map.snapshot;
  const layout = serverMap?.layout ?? EMPTY_LAYOUT;
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
  const operateConsumables = async (command: ConsumableCommand) => consumableNotice(command.kind, await link.operateConsumables(command));
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
  const [heights, setHeights] = useState(demoHeights);
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
        return (
          <FleetPage
            entries={agvEntries}
            types={agvTypes}
            carriers={demoCarriers}
            defaults={{ chargeAt: 20, resumeAt: 80 }}
            permissions={permissions}
            onAddAgv={(d) => setAgvEntries((es) => [...es, { id: agvId(d.id), type: d.type, carrier: d.carrier, control: 'detached', registration: 'both' }])}
            onChangeAgv={(id, c) => setAgvEntries((es) => es.map((e) => (e.id === id ? { ...e, ...c } : e)))}
            onAlignAgv={(id, c) => setAgvEntries((es) => es.map((e) => (e.id === id ? { ...e, ...c, control: 'detached', registration: 'both' } : e)))}
            onDeleteAgv={(id) => setAgvEntries((es) => es.filter((e) => e.id !== id))}
            onSaveType={(original, t) => setAgvTypes((ts) => (original === null ? [...ts, t] : ts.map((x) => (x.name === original ? t : x))))}
            onDeleteType={(name) => setAgvTypes((ts) => ts.filter((t) => t.name !== name))}
          />
        );
      case 'consumableConfig':
        return (
          <ToolSettingsPage
            processKinds={processKinds}
            varieties={configVarieties}
            devices={deviceConfigs}
            stations={stationConfigs}
            carriers={demoCarriers}
            transferPanels={transferPanels}
            permissions={permissions}
            onSaveProcessKind={(original, k) => setProcessKinds((ks) => (original === null ? [...ks, k] : ks.map((x) => (x.name === original ? k : x))))}
            onDeleteProcessKind={(name) => setProcessKinds((ks) => ks.filter((k) => k.name !== name))}
            onSaveVariety={(original, v) =>
              setConfigVarieties((vs) =>
                original === null ? [...vs, { ...v, ids: [], stockedIds: [], inUse: false }] : vs.map((x) => (x.name === original ? { ...x, ...v } : x)),
              )
            }
            onDeleteVariety={(name) => setConfigVarieties((vs) => vs.filter((v) => v.name !== name))}
            onAddId={(name, id) => setConfigVarieties((vs) => vs.map((v) => (v.name === name ? { ...v, ids: [...v.ids, id] } : v)))}
            onRemoveId={(name, id) => setConfigVarieties((vs) => vs.map((v) => (v.name === name ? { ...v, ids: v.ids.filter((i) => i !== id) } : v)))}
            onSaveDevice={(original, d) =>
              setDeviceConfigs((ds) =>
                original === null ? [...ds, { ...d, occupied: [], busy: false }] : ds.map((x) => (x.id === original ? { ...x, ...d } : x)),
              )
            }
            onDeleteDevice={(id) => setDeviceConfigs((ds) => ds.filter((d) => d.id !== id))}
            onSaveStation={(original, s) =>
              setStationConfigs((ss) => (original === null ? [...ss, { ...s, busy: false }] : ss.map((x) => (x.id === original ? { ...x, ...s } : x))))
            }
            onDeleteStation={(id) => setStationConfigs((ss) => ss.filter((s) => s.id !== id))}
          />
        );
      case 'users':
        return (
          <UsersPage
            users={users}
            roles={roles}
            permissions={permissions}
            onChangeRole={(id, role) => setUsers((us) => us.map((u) => (u.id === id ? { ...u, role } : u)))}
            onUnlock={(id) => setUsers((us) => us.map((u) => (u.id === id ? { ...u, state: { kind: 'active' } } : u)))}
            onResetPassword={(id) => {
              setUsers((us) => us.map((u) => (u.id === id ? { ...u, state: { kind: 'mustChangePassword' } } : u)));
              return Promise.resolve(demoPassword());
            }}
            onAddUser={(id) => {
              setUsers((us) => [...us, { id, role: roles.find((r) => r.isDefault)?.id ?? '', lastLogin: null, state: { kind: 'mustChangePassword' } }]);
              return Promise.resolve(demoPassword());
            }}
            onSaveRole={(original, r) =>
              setRoles((rs) =>
                original === null
                  ? [...rs, { ...r, id: `role-${rs.length + 1}`, permissions: new Set(), isDefault: false, fixed: false }]
                  : rs.map((x) => (x.id === original ? { ...x, ...r } : x)),
              )
            }
            onDeleteRole={(id) => setRoles((rs) => rs.filter((r) => r.id !== id))}
            onMakeDefault={(id) => setRoles((rs) => rs.map((r) => ({ ...r, isDefault: r.id === id })))}
            onSavePermissions={(saved) => setRoles((rs) => rs.map((r) => ({ ...r, permissions: saved.get(r.id) ?? r.permissions })))}
          />
        );
      case 'mapEdit':
        return (
          <MapEditPage
            saved={mapDraft}
            draftVersion={demoPublished.version + 1}
            savedAt={draftSavedAt}
            publishedVersion={demoPublished.version}
            equipment={[...deviceConfigs, ...stationConfigs]}
            heights={heights}
            running={run !== 'stopped'} // わからないときは、運転中として扱う（公開の決まりが厳しい側）
            travelingAgvs={agvs.filter((a) => a.operation === 'traveling').map((a) => a.id)}
            permissions={permissions}
            onSaveDraft={(d) => {
              setMapDraft(d);
              setDraftSavedAt(new Date());
            }}
            onValidate={(d) => Promise.resolve(demoValidate(d))}
            onPublish={(d) => {
              setDemoPublished((p) => ({ version: p.version + 1, layout: d.layout }));
              setMapDraft(d);
              setDraftSavedAt(null);
            }}
            onSaveHeight={(panel, mm) => setHeights((hs) => ({ ...hs, [panel]: mm === null ? null : { heightMm: mm, source: 'manual', at: new Date() } }))}
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
