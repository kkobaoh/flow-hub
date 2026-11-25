import type { Meta, StoryObj } from '@storybook/react-vite';
import { demoStations, demoVarieties } from '../app/demo/demo';
import { initialRoles } from '../features/auth/permissions';
import { StationPage } from './StationPage';

const meta = {
  title: 'ステーションの在庫/StationScreen',
  component: StationPage,
  args: {
    stations: demoStations,
    selected: null,
    varieties: demoVarieties,
    permissions: initialRoles.運用,
    offline: false,
    onSelect: () => {},
    onShip: () => Promise.resolve(null),
    onFix: () => Promise.resolve(null),
  },
  decorators: [
    (Story) => (
      <div className="flex flex-col gap-4 bg-ground p-5 font-sans text-graphite">
        <Story />
      </div>
    ),
  ],
} satisfies Meta<typeof StationPage>;

export default meta;

type Story = StoryObj<typeof meta>;

/** 要確認の消耗品が1個ある。「確かめて直す」で直す画面を開く。 */
export const Default: Story = {};

/** 閲覧のみのロール。出庫も直すのもできない。 */
export const ViewOnly: Story = {
  args: { permissions: initialRoles.閲覧のみ },
};
