import { useState, type ReactNode } from 'react';
import {
  applyDemoSetting,
  demoAgvs,
  demoConsumables,
  demoDestinations,
  demoDevices,
  demoEquipment,
  demoMessages,
  demoReserved,
  demoRoutes,
  demoStations,
  demoTransports,
  demoVarieties,
} from './app/demo';
import { DevicesScreen } from './app/DevicesScreen';
import { matchesQuery } from './app/listing';
import { MapScreen } from './app/MapScreen';
import { useRoute, type Route } from './app/navigation';
import { initialRoles } from './app/permissions';
import { ScreenFrame } from './app/ScreenFrame';
import { screens } from './app/screens';
import { StationScreen } from './app/StationScreen';
import { TransportsScreen } from './app/TransportsScreen';
import { sampleLayout } from './map/sampleLayout';

const accepted = () => Promise.resolve('accepted' as const);
const user = { name: '[ユーザー名]', role: '保守' } as const;
const permissions = initialRoles.保守;
const mapVersion = 'マップ 版3（公開済み）';

/**
 * 画面の入り口。サーバーとつながるまでは、見本のデータで画面を動かす。
 * 操作した結果は、この中で覚えて、画面が切り替わるようにしている。
 */
export function App() {
  const [route, navigate] = useRoute();
  const [running, setRunning] = useState(true);
  const [messages, setMessages] = useState(demoMessages);
  const [agvs, setAgvs] = useState(demoAgvs);
  const [transports, setTransports] = useState(demoTransports);
  const [devices, setDevices] = useState(demoDevices);
  const [stations, setStations] = useState(demoStations);
  const frame = {
    running,
    user,
    permissions,
    mapVersion,
    messages,
    onMarkAllSeen: () => setMessages((ms) => ms.map((m) => ({ ...m, unread: false }))),
    onNavigate: navigate,
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
      default:
        return <p className="m-0 rounded-[10px] border border-line bg-paper p-5 text-[15px] text-ink-muted">この画面は、これから作ります。</p>;
    }
  };

  return (
    <>
      <p className="m-0 bg-estop-plate px-3 py-1 text-center text-xs font-bold text-graphite">
        見本のデータで表示しています（サーバーとは、まだつながっていません）
      </p>
      {route.screen === 'map' ? (
        <MapScreen
          {...frame}
          layout={sampleLayout}
          agvs={agvs}
          equipment={demoEquipment}
          reservedPanels={demoReserved}
          routes={demoRoutes}
          destinations={demoDestinations}
          searchConsumables={(query) => demoConsumables.filter((c) => matchesQuery(c.id, query))}
          onOpenEquipment={(screen, id) => navigate(screen, id)}
          onOpenConsumable={(id) => navigate('consumables', id)}
          onStart={() => setRunning(true)}
          onStop={() => setRunning(false)}
          onEmergencyStop={() => {}}
          onChangeControl={(id, control) => setAgvs((as) => as.map((a) => (a.id === id ? { ...a, control } : a)))}
          onAlarmClear={() => {}}
          onPositionReset={() => {}}
          onManualMove={accepted}
          onManualStep={accepted}
          onManualCarrier={accepted}
        />
      ) : (
        <ScreenFrame {...frame} current={route.screen} title={screens.find((s) => s.key === route.screen)?.title ?? ''}>
          {content(route)}
        </ScreenFrame>
      )}
    </>
  );
}
