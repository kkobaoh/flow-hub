import type { Meta, StoryObj } from '@storybook/react-vite';
import { AppHeader } from './AppHeader';

const meta = {
  title: '画面の枠/AppHeader',
  component: AppHeader,
  args: {
    run: 'running',
    user: { name: '[ユーザー名]', role: '運用' },
    current: 'map',
    mapVersion: 'マップ 版3（公開済み）',
    onNavigate: () => {},
    onChangePassword: () => {},
    onLogout: () => {},
  },
  decorators: [
    (Story) => (
      <div className="min-h-[520px] bg-ground">
        <Story />
      </div>
    ),
  ],
} satisfies Meta<typeof AppHeader>;

export default meta;

type Story = StoryObj<typeof meta>;

/** 運転中。右のメニューを押すと、画面の一覧が開く。 */
export const Running: Story = {};

/** 停止中。 */
export const Stopped: Story = {
  args: { run: 'stopped' },
};

/** サーバーから運転状態が届く前。 */
export const Unknown: Story = {
  args: { run: 'unknown' },
};
