import type { Meta, StoryObj } from '@storybook/react-vite';
import { FleetPage } from './FleetPage';
import { demoAgvEntries, demoAgvTypes, demoCarriers } from '../app/demo/demo';
import { initialRoles } from '../features/auth/permissions';

const meta = {
  title: 'AGV と AGV の種類の登録/AgvsScreen',
  component: FleetPage,
  args: {
    entries: demoAgvEntries,
    types: demoAgvTypes,
    carriers: demoCarriers,
    defaults: { chargeAt: 20, resumeAt: 80 },
    permissions: initialRoles.管理者,
    onAddAgv: () => {},
    onChangeAgv: () => {},
    onAlignAgv: () => {},
    onDeleteAgv: () => {},
    onSaveType: () => {},
    onDeleteType: () => {},
  },
  decorators: [
    (Story) => (
      <div className="flex flex-col gap-4 bg-ground p-5 font-sans text-graphite">
        <Story />
      </div>
    ),
  ],
} satisfies Meta<typeof FleetPage>;

export default meta;

type Story = StoryObj<typeof meta>;

/** AGV-4 は、走行の側にだけ登録されている（登録をそろえる）。 */
export const Default: Story = {};

/** 管理者でないロール。見るだけ。 */
export const Maintainer: Story = {
  args: { permissions: initialRoles.保守 },
};
