import type { Meta, StoryObj } from '@storybook/react-vite';
import { ConfigScreen } from './ConfigScreen';
import { demoCarriers, demoConfigVarieties, demoDeviceConfigs, demoProcessKinds, demoStationConfigs } from './demo';
import { initialRoles } from './permissions';

const meta = {
  title: '消耗品の構成/ConfigScreen',
  component: ConfigScreen,
  args: {
    processKinds: demoProcessKinds,
    varieties: demoConfigVarieties,
    devices: demoDeviceConfigs,
    stations: demoStationConfigs,
    carriers: demoCarriers,
    transferPanels: ['D1', 'D2', 'SP', 'ST'],
    permissions: initialRoles.管理者,
    onSaveProcessKind: () => {},
    onDeleteProcessKind: () => {},
    onSaveVariety: () => {},
    onDeleteVariety: () => {},
    onAddId: () => {},
    onRemoveId: () => {},
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
} satisfies Meta<typeof ConfigScreen>;

export default meta;

type Story = StoryObj<typeof meta>;

/** 加工の種類、品種、装置、ステーションをタブで切り替える。 */
export const Default: Story = {};

/** 管理者でないロール。見るだけ。 */
export const Maintainer: Story = {
  args: { permissions: initialRoles.保守 },
};
