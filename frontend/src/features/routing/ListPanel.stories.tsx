import type { Meta, StoryObj } from '@storybook/react-vite';
import { demoAgvs, demoEquipment } from '../../app/demo/demo';
import { ListPanel } from './ListPanel';

const meta = {
  title: '画面の枠/ListPanel',
  component: ListPanel,
  args: {
    agvs: demoAgvs,
    equipment: demoEquipment,
    searchConsumables: () => [],
    onSelect: () => {},
    onHighlight: () => {},
    onClose: () => {},
  },
  decorators: [
    (Story) => (
      <div className="w-[380px] bg-ground">
        <Story />
      </div>
    ),
  ],
} satisfies Meta<typeof ListPanel>;

export default meta;

type Story = StoryObj<typeof meta>;

/** AGV・装置・ステーション・消耗品のタブ。気をつけるものを上に、残りは番号の順に並べる。 */
export const Default: Story = {};
