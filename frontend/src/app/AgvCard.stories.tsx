import type { Meta, StoryObj } from '@storybook/react-vite';
import { agvId } from '../map/agv';
import { heading, panelId } from '../map/model';
import { AgvCard, type AgvDetail } from './AgvCard';
import { initialRoles } from './permissions';

const agv: AgvDetail = {
  id: agvId('AGV-1'),
  label: '1',
  panel: panelId('P3'),
  heading: heading(90),
  status: { activity: 'moving', charging: false, carrierMoving: false },
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
    agv: { ...agv, panel: panelId('SB2'), status: { activity: 'idle', charging: true, carrierMoving: false }, operation: 'idle', transport: null, batteryPercent: 35 },
  },
};

/** 切り離し中。どのコマンドも送らない。 */
export const Detached: Story = {
  args: {
    permissions: initialRoles.保守,
    agv: { ...agv, control: 'detached', operation: 'idle', transport: null, batteryPercent: null },
  },
};
