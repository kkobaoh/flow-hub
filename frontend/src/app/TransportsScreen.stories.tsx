import type { Meta, StoryObj } from '@storybook/react-vite';
import { demoTransports } from './demo';
import { initialRoles } from './permissions';
import { TransportsScreen } from './TransportsScreen';

const meta = {
  title: '搬送の一覧/TransportsScreen',
  component: TransportsScreen,
  args: { transports: demoTransports, permissions: initialRoles.運用, onCancel: () => {}, onRetry: () => {} },
  decorators: [
    (Story) => (
      <div className="flex flex-col gap-4 bg-ground p-5 font-sans text-graphite">
        <Story />
      </div>
    ),
  ],
} satisfies Meta<typeof TransportsScreen>;

export default meta;

type Story = StoryObj<typeof meta>;

/** 中断した搬送があるので、「中断した」のタブから開く。 */
export const Default: Story = {};

/** 閲覧のみのロール。ボタンは押せず、理由を出す。 */
export const ViewOnly: Story = {
  args: { permissions: initialRoles.閲覧のみ },
};

/** 搬送がないとき。 */
export const Empty: Story = {
  args: { transports: [] },
};
