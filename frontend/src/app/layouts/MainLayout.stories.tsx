import type { Meta, StoryObj } from '@storybook/react-vite';
import { demoMessages } from '../demo/demo';
import { initialRoles } from '../../features/auth/permissions';
import { MainLayout } from './MainLayout';

const meta = {
  title: '画面の枠/ScreenFrame',
  component: MainLayout,
  parameters: { layout: 'fullscreen' },
  args: {
    current: 'transports',
    title: '搬送の一覧',
    run: 'running',
    user: { name: '[ユーザー名]', role: '運用' },
    permissions: initialRoles.運用,
    mapVersion: 'マップ 版3（公開済み）',
    messages: demoMessages,
    onMarkAllSeen: () => {},
    onNavigate: () => {},
    onChangePassword: () => {},
    onLogout: () => {},
    children: <p className="m-0 rounded-[10px] border border-line bg-paper p-5 text-[15px]">画面の中身</p>,
  },
} satisfies Meta<typeof MainLayout>;

export default meta;

type Story = StoryObj<typeof meta>;

/** マップ以外の画面の枠。メッセージの列はたたんでおき、タブで重ねて開く。 */
export const Default: Story = {};
