import { useMemo, useRef, useState, type ReactNode } from 'react';
import {
  applyDemoSetting,
  demoAgvEntries,
  demoAgvTypes,
  demoCarriers,
  demoConfigVarieties,
  demoConsumables,
  demoDeviceConfigs,
  demoDevices,
  demoHeights,
  demoMapDraft,
  demoEquipment,
  demoMessages,
  demoProcessKinds,
  demoRecords,
  demoRoles,
  demoStationConfigs,
  demoStations,
  demoTransports,
  demoUsers,
  demoValidate,
  demoVarieties,
} from './app/demo';
import { AgvsScreen } from './app/AgvsScreen';
import { ConnectionBanner } from './app/ConnectionBanner';
import { ConfigScreen } from './app/ConfigScreen';
import type { ConsumableRecord } from './app/consumables';
import { ConsumablesScreen } from './app/ConsumablesScreen';
import { ConfirmDialog } from './app/ConfirmDialog';
import { DevicesScreen } from './app/DevicesScreen';
import { matchesQuery } from './app/listing';
import { MapEditScreen } from './app/MapEditScreen';
import { MapScreen } from './app/MapScreen';
import { useRoute, type Route } from './app/navigation';
import { initialRoles } from './app/permissions';
import { ScreenFrame } from './app/ScreenFrame';
import { screens, type ScreenKey } from './app/screens';
import { StationScreen } from './app/StationScreen';
import { TransportsScreen } from './app/TransportsScreen';
import { UsersScreen } from './app/UsersScreen';
import { agvId, type AgvId } from './map/agv';
import { panelOptions } from './app/panelOptions';
import type { Message } from './app/MessageRail';
import type { CommandResult, ServerLink } from './server/link';
import { commandNotice, toManualOutcome, type AgvCommand, type AgvCommandResult } from './server/agvCommands';
import { mapOverlays, toAgvDetails } from './server/agvs';
import { runIndicator } from './server/systemStatus';
import { useServer } from './server/useServer';
import { sampleLayout } from './map/sampleLayout';

const user = { name: '[ユーザー名]', role: '管理者' } as const;
const permissions = initialRoles.管理者;

/** 見本の画面で見せる仮のパスワード（本物は、サーバーが作る）。 */
function demoPassword(): string {
  const letters = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789';
  return Array.from(crypto.getRandomValues(new Uint32Array(12)), (n) => letters[n % letters.length]).join('');
}
const transferPanels = sampleLayout.panels.filter((p) => p.type === 'Transfer' || p.type === 'SpecialTransfer').map((p): string => p.id);

/** 運転の切り替えを送れなかったときに出すメッセージ。 */
const commandFailed = {
  start: { title: 'スタートできたかわかりません', detail: 'サーバーから応答がなかったか、受け付けられませんでした。表示の運転状態を確かめてください。' },
  stop: { title: '停止できたかわかりません', detail: 'サーバーから応答がなかったか、受け付けられませんでした。表示の運転状態を確かめてください。' },
} as const;

export interface AppProps {
  /** サーバーとのつながり（本物は main.tsx で作る。テストと Storybook は偽物）。 */
  readonly link: ServerLink;
}

/**
 * 画面の入り口。運転状態はサーバーとつないで、届いた様子を出す。ほかは、まだ見本のデータで画面を動かす。
 * 見本のデータを操作した結果は、この中で覚えて、画面が切り替わるようにしている。
 */
export function App({ link }: AppProps) {
  const [route, navigate] = useRoute();
  const server = useServer(link);
  const run = runIndicator(server.status);
  const [sending, setSending] = useState(false);
  const [messages, setMessages] = useState<readonly Message[]>(demoMessages);
  const messageCount = useRef(0);
  // AGV ごとの、送って結果を待っている操作の数と、受け付けられなかったときの知らせ
  const [sendingAgvs, setSendingAgvs] = useState<ReadonlyMap<AgvId, number>>(new Map());
  const [agvNotices, setAgvNotices] = useState<ReadonlyMap<AgvId, string>>(new Map());
  const offline = server.connection !== 'connected';
  const [transports, setTransports] = useState(demoTransports);
  const [devices, setDevices] = useState(demoDevices);
  const [stations, setStations] = useState(demoStations);
  const [records, setRecords] = useState(demoRecords);
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
  const [published, setPublished] = useState({ version: 3, layout: sampleLayout });
  const destinations = useMemo(() => panelOptions(published.layout), [published.layout]);
  // AGV は、サーバーから届いた様子を、描いているマップに合わせて使う（まだ届いていなければ、いない）
  const agvs = useMemo(
    () => (server.agvs.kind === 'waiting' ? [] : toAgvDetails(server.agvs.snapshot.agvs, published.layout)),
    [server.agvs, published.layout],
  );
  // 予約しているパネルと走る予定の経路も、サーバーから届いた AGV の様子から描く
  const overlays = useMemo(
    () => (server.agvs.kind === 'waiting' ? { reservedPanels: [], routes: [] } : mapOverlays(server.agvs.snapshot.agvs, published.layout)),
    [server.agvs, published.layout],
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
  const updateRecord = (id: string, change: (c: ConsumableRecord) => ConsumableRecord) => setRecords((rs) => rs.map((c) => (c.id === id ? change(c) : c)));
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
      const time = new Date().toLocaleTimeString('ja-JP', { hour: '2-digit', minute: '2-digit' });
      setMessages((ms) => [{ id: `run-${messageCount.current}`, time, ...commandFailed[kind], unread: true }, ...ms]);
    }
  };
  const frame = {
    run,
    user,
    permissions,
    mapVersion: `マップ 版${published.version}（公開済み）`,
    messages,
    onMarkAllSeen: () => setMessages((ms) => ms.map((m) => ({ ...m, unread: false }))),
    onNavigate: go,
    onChangePassword: () => {},
    onLogout: () => {},
  };

  const content = (r: Route): ReactNode => {
    switch (r.screen) {
      case 'transports':
        return (
          <TransportsScreen
            transports={transports}
            permissions={permissions}
            onCancel={(n) => setTransports((ts) => ts.filter((t) => t.number !== n))}
            onRetry={(n) => setTransports((ts) => ts.map((t) => (t.number === n ? { ...t, status: { stage: 'running' }, step: '搬送先へ移動', stepNote: 'やり直しました' } : t)))}
          />
        );
      case 'devices':
        return (
          <DevicesScreen
            devices={devices}
            selected={r.target}
            varieties={demoVarieties}
            permissions={permissions}
            onSelect={(id) => navigate('devices', id)}
            onSaveSetting={(id, rows) => setDevices((ds) => ds.map((d) => (d.id === id ? applyDemoSetting(d, rows) : d)))}
          />
        );
      case 'station':
        return (
          <StationScreen
            stations={stations}
            selected={r.target}
            varieties={demoVarieties}
            permissions={permissions}
            onSelect={(id) => navigate('station', id)}
            onShip={(sid, item) => setStations((ss) => ss.map((s) => (s.id === sid ? { ...s, items: s.items.filter((i) => i.id !== item) } : s)))}
            onFix={(sid, item, fix) =>
              setStations((ss) =>
                ss.map((s) =>
                  s.id === sid
                    ? { ...s, items: s.items.map((i) => (i.id === item ? { ...i, variety: fix.variety, condition: fix.condition, state: { kind: 'available' } } : i)) }
                    : s,
                ),
              )
            }
          />
        );
      case 'consumables':
        return (
          <ConsumablesScreen
            key={r.target ?? ''}
            records={records}
            selected={r.target}
            stations={stations.map((s) => s.id)}
            devices={devices}
            varieties={demoVarieties}
            permissions={permissions}
            onOpen={(id) => navigate('consumables', id)}
            onNominate={(id, device) => updateRecord(id, (c) => ({ ...c, nomination: { device, assigned: false } }))}
            onCancelNomination={(id) => updateRecord(id, (c) => ({ ...c, nomination: null }))}
            onRegister={(input) => setRecords((rs) => [...rs, { ...input, processKind: null, needsCheck: false, reservation: null, nomination: null, shipping: false, history: [] }])}
            onEdit={(input) => updateRecord(input.id, (c) => ({ ...c, ...input }))}
            onDelete={(id) => {
              setRecords((rs) => rs.filter((c) => c.id !== id));
              navigate('consumables');
            }}
          />
        );
      case 'agvs':
        return (
          <AgvsScreen
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
          <ConfigScreen
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
          <UsersScreen
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
          <MapEditScreen
            saved={mapDraft}
            draftVersion={published.version + 1}
            savedAt={draftSavedAt}
            publishedVersion={published.version}
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
              setPublished((p) => ({ version: p.version + 1, layout: d.layout }));
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
      <p className="m-0 bg-estop-plate px-3 py-1 text-center text-xs font-bold text-graphite">
        運転状態と AGV のほかは、見本のデータです
      </p>
      {route.screen === 'map' ? (
        <MapScreen
          {...frame}
          layout={published.layout}
          agvs={agvs}
          equipment={demoEquipment.filter((e) => published.layout.panels.some((p) => p.id === e.panel))}
          reservedPanels={overlays.reservedPanels}
          routes={overlays.routes}
          destinations={destinations}
          searchConsumables={(query) => demoConsumables.filter((c) => matchesQuery(c.id, query))}
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
        <ScreenFrame {...frame} current={route.screen} title={screens.find((s) => s.key === route.screen)?.title ?? ''}>
          {content(route)}
        </ScreenFrame>
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
