import type { Meta, StoryObj } from '@storybook/react-vite';
import { ToolSettingsPage } from './ToolSettingsPage';
import { demoCarriers, demoConfigVarieties, demoDeviceConfigs, demoProcessKinds, demoStationConfigs } from '../app/demo/demo';
import { initialRoles } from '../features/auth/permissions';

const meta = {
  title: '消耗品の構成/ConfigScreen',
  component: ToolSettingsPage,
  args: {
    processKinds: demoProcessKinds,
    varieties: demoConfigVarieties,
    devices: demoDeviceConfigs,
    stations: demoStationConfigs,
    carriers: demoCarriers,
    transferPanels: ['D1', 'D2', 'SP', 'ST'],
    permissions: initialRoles.管理者,
    offline: false,
    onSaveProcessKind: () => Promise.resolve(null),
    onDeleteProcessKind: () => Promise.resolve(null),
    onSaveVariety: () => Promise.resolve(null),
    onDeleteVariety: () => Promise.resolve(null),
    onAddId: () => Promise.resolve(null),
    onRemoveId: () => Promise.resolve(null),
    onSaveDevice: () => {},
    onDeleteDevice: () => {},
    onSaveStation: () => {},
    onDeleteStation: () => {},
  },
  decorators: [
    (Story) => (
      <div className="flex flex-col gap-4 bg-ground p-5 font-sans text-graphite">
        <Story />
      </div>
    ),
  ],
} satisfies Meta<typeof ToolSettingsPage>;

export default meta;

type Story = StoryObj<typeof meta>;

/** 加工の種類、品種、装置、ステーションをタブで切り替える。 */
export const Default: Story = {};

/** 管理者でないロール。見るだけ。 */
export const Maintainer: Story = {
  args: { permissions: initialRoles.保守 },
};

/** サーバーとつながっていない。加工の種類と品種は変えられない。 */
export const Offline: Story = {
  args: { offline: true },
};
