import { useState } from 'react';
import { demoAgvs, demoDestinations, demoEquipment, demoMessages, demoReserved, demoRoutes } from './app/demo';
import { MapScreen } from './app/MapScreen';
import { initialRoles } from './app/permissions';
import { sampleLayout } from './map/sampleLayout';

const accepted = () => Promise.resolve('accepted' as const);

/**
 * 画面の入り口。サーバーとつながるまでは、見本のデータで、全体の様子（マップ）の画面を動かす。
 * 運転状態、見たメッセージ、コントロールステータスだけは、この中で覚えて切り替わるようにしている。
 */
export function App() {
  const [running, setRunning] = useState(true);
  const [messages, setMessages] = useState(demoMessages);
  const [agvs, setAgvs] = useState(demoAgvs);

  return (
    <>
      <p className="m-0 bg-estop-plate px-3 py-1 text-center text-xs font-bold text-graphite">
        見本のデータで表示しています（サーバーとは、まだつながっていません）
      </p>
      <MapScreen
        layout={sampleLayout}
        agvs={agvs}
        equipment={demoEquipment}
        onOpenEquipment={() => {}}
        reservedPanels={demoReserved}
        routes={demoRoutes}
        messages={messages}
        running={running}
        user={{ name: '[ユーザー名]', role: '保守' }}
        permissions={initialRoles.保守}
        mapVersion="マップ 版3（公開済み）"
        destinations={demoDestinations}
        onNavigate={() => {}}
        onChangePassword={() => {}}
        onLogout={() => {}}
        onStart={() => setRunning(true)}
        onStop={() => setRunning(false)}
        onMarkAllSeen={() => setMessages((ms) => ms.map((m) => ({ ...m, unread: false })))}
        onEmergencyStop={() => {}}
        onChangeControl={(id, control) => setAgvs((as) => as.map((a) => (a.id === id ? { ...a, control } : a)))}
        onAlarmClear={() => {}}
        onPositionReset={() => {}}
        onManualMove={accepted}
        onManualStep={accepted}
        onManualCarrier={accepted}
      />
    </>
  );
}
