import { useState } from 'react';
import { demoAgvs, demoConsumables, demoDestinations, demoEquipment, demoMessages, demoReserved, demoRoutes, demoTransports } from './app/demo';
import { matchesQuery } from './app/listing';
import { MapScreen } from './app/MapScreen';
import { useScreen } from './app/navigation';
import { initialRoles } from './app/permissions';
import { ScreenFrame } from './app/ScreenFrame';
import { screens } from './app/screens';
import { TransportsScreen } from './app/TransportsScreen';
import { sampleLayout } from './map/sampleLayout';

const accepted = () => Promise.resolve('accepted' as const);
const user = { name: '[ユーザー名]', role: '保守' } as const;
const permissions = initialRoles.保守;
const mapVersion = 'マップ 版3（公開済み）';

/**
 * 画面の入り口。サーバーとつながるまでは、見本のデータで画面を動かす。
 * 運転状態、見たメッセージ、コントロールステータスだけは、この中で覚えて切り替わるようにしている。
 */
export function App() {
  const [screen, navigate] = useScreen();
  const [running, setRunning] = useState(true);
  const [messages, setMessages] = useState(demoMessages);
  const [agvs, setAgvs] = useState(demoAgvs);
  const [transports, setTransports] = useState(demoTransports);
  const markAllSeen = () => setMessages((ms) => ms.map((m) => ({ ...m, unread: false })));
  const frame = {
    running,
    user,
    permissions,
    mapVersion,
    messages,
    onMarkAllSeen: markAllSeen,
    onNavigate: navigate,
    onChangePassword: () => {},
    onLogout: () => {},
  };

  return (
    <>
      <p className="m-0 bg-estop-plate px-3 py-1 text-center text-xs font-bold text-graphite">
        見本のデータで表示しています（サーバーとは、まだつながっていません）
      </p>
      {screen === 'map' ? (
        <MapScreen
          {...frame}
          layout={sampleLayout}
          agvs={agvs}
          equipment={demoEquipment}
          reservedPanels={demoReserved}
          routes={demoRoutes}
          destinations={demoDestinations}
          searchConsumables={(query) => demoConsumables.filter((c) => matchesQuery(c.id, query))}
          onOpenEquipment={(target) => navigate(target)}
          onOpenConsumable={() => navigate('consumables')}
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
        <ScreenFrame {...frame} current={screen} title={screens.find((s) => s.key === screen)?.title ?? ''}>
          {screen === 'transports' ? (
            <TransportsScreen
              transports={transports}
              permissions={permissions}
              onCancel={(n) => setTransports((ts) => ts.filter((t) => t.number !== n))}
              onRetry={(n) => setTransports((ts) => ts.map((t) => (t.number === n ? { ...t, status: { stage: 'running' }, step: '搬送先へ移動', stepNote: 'やり直しました' } : t)))}
            />
          ) : (
            <p className="m-0 rounded-[10px] border border-line bg-paper p-5 text-[15px] text-ink-muted">この画面は、これから作ります。</p>
          )}
        </ScreenFrame>
      )}
    </>
  );
}
