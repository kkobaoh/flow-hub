import type { Meta, StoryObj } from '@storybook/react-vite';
import { demoRoles, demoUsers } from './demo';
import { initialRoles } from './permissions';
import { UsersScreen } from './UsersScreen';

const meta = {
  title: 'ユーザーとロールの管理/UsersScreen',
  component: UsersScreen,
  args: {
    users: demoUsers,
    roles: demoRoles,
    permissions: initialRoles.管理者,
    onChangeRole: () => {},
    onUnlock: () => {},
    onResetPassword: () => Promise.resolve('Kp7mWq2xRt9z'),
    onAddUser: () => Promise.resolve('Hn4sLc8vYb3e'),
    onSaveRole: () => {},
    onDeleteRole: () => {},
    onMakeDefault: () => {},
    onSavePermissions: () => {},
  },
  decorators: [
    (Story) => (
      <div className="flex flex-col gap-4 bg-ground p-5 font-sans text-graphite">
        <Story />
      </div>
    ),
  ],
} satisfies Meta<typeof UsersScreen>;

export default meta;

type Story = StoryObj<typeof meta>;

/** ユーザーの一覧。maint01 はロック中、admin01 は最後の管理者なのでロールを付け替えられない。 */
export const Default: Story = {};

/** 管理者でないロール。見るだけ。 */
export const Maintainer: Story = {
  args: { permissions: initialRoles.保守 },
};
