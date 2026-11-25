import type { Meta, StoryObj } from '@storybook/react-vite';
import { ConsumablesPage } from './ConsumablesPage';
import { demoDevices, demoRecords, demoVarieties } from '../app/demo/demo';
import { initialRoles } from '../features/auth/permissions';

const meta = {
  title: '消耗品の検索と手入力/ConsumablesScreen',
  component: ConsumablesPage,
  args: {
    records: demoRecords,
    selected: 'T-0101',
    stations: ['ST-1'],
    devices: demoDevices,
    varieties: demoVarieties,
    permissions: initialRoles.保守,
    offline: false,
    manualEntryNote: null,
    onOpen: () => {},
    onNominate: () => Promise.resolve(null),
    onCancelNomination: () => Promise.resolve(null),
    onRegister: () => {},
    onEdit: () => {},
    onDelete: () => {},
  },
  decorators: [
    (Story) => (
      <div className="flex flex-col gap-4 bg-ground p-5 font-sans text-graphite">
        <Story />
      </div>
    ),
  ],
} satisfies Meta<typeof ConsumablesPage>;

export default meta;

type Story = StoryObj<typeof meta>;

/** 装置のスロットにある消耗品。これまでの動きを出す。 */
export const InDevice: Story = {};

/** ステーションにある新品。装置へ届けるものに指名できる。 */
export const AtStation: Story = {
  args: { selected: 'T-0110' },
};

/** AGV に積んでいる消耗品。記録は直せない。 */
export const OnAgv: Story = {
  args: { selected: 'T-0207' },
};

/** 運用のロール。手入力は使えない。 */
export const Operator: Story = {
  args: { selected: 'T-0110', permissions: initialRoles.運用 },
};

/** まだ探していないとき。 */
export const Empty: Story = {
  args: { selected: null },
};
