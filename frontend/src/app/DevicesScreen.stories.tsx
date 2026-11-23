import type { Meta, StoryObj } from '@storybook/react-vite';
import { demoDevices, demoVarieties } from './demo';
import { DevicesScreen } from './DevicesScreen';
import { initialRoles } from './permissions';

const meta = {
  title: '装置とストッカー/DevicesScreen',
  component: DevicesScreen,
  args: {
    devices: demoDevices,
    selected: null,
    varieties: demoVarieties,
    permissions: initialRoles.運用,
    onSelect: () => {},
    onSaveSetting: () => {},
  },
  decorators: [
    (Story) => (
      <div className="flex flex-col gap-4 bg-ground p-5 font-sans text-graphite">
        <Story />
      </div>
    ),
  ],
} satisfies Meta<typeof DevicesScreen>;

export default meta;

type Story = StoryObj<typeof meta>;

/** ストッカーが2つ、スロットが3つずつの装置。回収を頼んだものや、ID が食い違ったものがある。 */
export const Default: Story = {};

/** プレートに載せたまま置く装置（スロットがない）。 */
export const Plate: Story = {
  args: { selected: 'DEV-3' },
};

/** 閲覧のみのロール。設定は変えられない。 */
export const ViewOnly: Story = {
  args: { permissions: initialRoles.閲覧のみ },
};
