import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { RunSwitch } from './RunSwitch';

const meta = {
  title: '画面の枠/RunSwitch',
  component: RunSwitch,
  args: { run: 'running', disabled: false, onStart: () => {}, onStop: () => {} },
  decorators: [
    (Story) => (
      <div className="flex min-h-[320px] items-end justify-end bg-floor p-3">
        <Story />
      </div>
    ),
  ],
} satisfies Meta<typeof RunSwitch>;

export default meta;

type Story = StoryObj<typeof meta>;

function Live() {
  const [running, setRunning] = useState(true);
  return <RunSwitch run={running ? 'running' : 'stopped'} disabled={false} onStart={() => setRunning(true)} onStop={() => setRunning(false)} />;
}

/** 押すと確かめる画面が出て、「停止する」「スタートする」で切り替わる。 */
export const Default: Story = {
  render: () => <Live />,
};

/** サーバーとつながっていないときは、押せない。 */
export const Disabled: Story = {
  args: { disabled: true },
};

/** 運転状態がまだ届いていないときは、どちらも押されていない形にして、押せない。 */
export const Unknown: Story = {
  args: { run: 'unknown' },
};
