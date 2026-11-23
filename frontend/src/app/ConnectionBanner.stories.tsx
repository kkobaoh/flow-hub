import type { Meta, StoryObj } from '@storybook/react-vite';
import { ConnectionBanner } from './ConnectionBanner';

const meta = {
  title: '画面の枠/ConnectionBanner',
  component: ConnectionBanner,
  args: { connection: 'reconnecting' },
} satisfies Meta<typeof ConnectionBanner>;

export default meta;

type Story = StoryObj<typeof meta>;

/** 切れて、つなぎ直しているあいだ。 */
export const Reconnecting: Story = {};

/** 初めてつないでいるあいだ。 */
export const Connecting: Story = {
  args: { connection: 'connecting' },
};
