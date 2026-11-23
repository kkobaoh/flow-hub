import type { Meta, StoryObj } from '@storybook/react-vite';
import { agvId } from '../map/agv';
import { heading, panelId } from '../map/model';
import { AgvCard, type AgvDetail } from './AgvCard';
import { ManualPanel } from './ManualPanel';
import { initialRoles } from './permissions';

const agv: AgvDetail = {
  id: agvId('AGV-1'),
  label: '1',
  position: { kind: 'onMap', panel: panelId('P3'), heading: heading(90) },
  status: { activity: 'moving', charging: 'notCharging', carrierMoving: false },
  control: 'auto',
  operation: 'traveling',
  transport: '#12 ST-1 から DEV-1 へ',
  batteryPercent: 72,
};

const meta = {
  title: '画面の枠/AgvCard',
  component: AgvCard,
  args: {
    agv,
    onChargingPin: false,
    permissions: initialRoles.運用,
    onClose: () => {},
    onEmergencyStop: () => {},
    onChangeControl: () => {},
    onAlarmClear: () => {},
    onPositionReset: () => {},
  },
  decorators: [
    (Story) => (
      <div className="w-[380px] bg-ground">
        <Story />
      </div>
    ),
  ],
} satisfies Meta<typeof AgvCard>;

export default meta;

type Story = StoryObj<typeof meta>;

/** 運用のロールで、走っている AGV。マニュアル・ロック・切り離しは選べず、理由を出す。 */
export const OperatorTraveling: Story = {};

/** 保守のロールで、待機中の AGV。復旧のボタンが出る。 */
export const MaintainerIdle: Story = {
  args: {
    permissions: initialRoles.保守,
    agv: { ...agv, status: { ...agv.status, activity: 'idle' }, operation: 'idle', transport: null },
  },
};

/** 充電パネルで充電中。 */
export const Charging: Story = {
  args: {
    onChargingPin: true,
    agv: { ...agv, position: { kind: 'onMap', panel: panelId('SB2'), heading: heading(90) }, status: { activity: 'idle', charging: 'charging', carrierMoving: false }, operation: 'idle', transport: null, batteryPercent: 35 },
  },
};

/** 保守のロールで、マニュアルの AGV。カードからマニュアル操作を送る。 */
export const MaintainerManual: Story = {
  args: {
    permissions: initialRoles.保守,
    agv: { ...agv, control: 'manual', status: { ...agv.status, activity: 'idle' }, operation: 'idle', transport: null },
    manual: (
      <ManualPanel
        input={{ operation: 'idle', activity: 'none', heading: heading(90) }}
        destinations={[
          { id: panelId('SB'), label: 'SB（待機パネル）' },
          { id: panelId('SB2'), label: 'SB2（待機パネル・充電ピンあり）' },
          { id: panelId('D1'), label: 'D1（DEV-1 の受け渡しパネル）' },
        ]}
        onMove={() => Promise.resolve('blocked')}
        onStep={() => Promise.resolve('accepted')}
        onCarrier={() => Promise.resolve('accepted')}
      />
    ),
  },
};

/** 切り離し中。どのコマンドも送らない。 */
export const Detached: Story = {
  args: {
    permissions: initialRoles.保守,
    agv: { ...agv, control: 'detached', operation: 'idle', transport: null, batteryPercent: null },
  },
};
