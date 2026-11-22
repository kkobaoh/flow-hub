import type { Meta, StoryObj } from '@storybook/react-vite';
import { demoEquipment } from './demo';
import { EquipmentCard } from './EquipmentCard';
import { initialRoles } from './permissions';

const byId = (id: string) => {
  const found = demoEquipment.find((e) => e.id === id);
  if (!found) {
    throw new Error(`${id} がありません`);
  }
  return found;
};

const meta = {
  title: '画面の枠/EquipmentCard',
  component: EquipmentCard,
  args: { equipment: byId('DEV-1'), permissions: initialRoles.運用, onClose: () => {}, onOpen: () => {} },
  decorators: [
    (Story) => (
      <div className="w-[380px] bg-ground">
        <Story />
      </div>
    ),
  ],
} satisfies Meta<typeof EquipmentCard>;

export default meta;

type Story = StoryObj<typeof meta>;

/** 装置。回収待ちと ID の食い違いがある。 */
export const Device: Story = {};

/** ステーション。要確認と補充待ちがある。 */
export const Station: Story = {
  args: { equipment: byId('ST-1') },
};

/** 気をつけることがない装置。 */
export const NothingToWatch: Story = {
  args: { equipment: byId('DEV-3') },
};
