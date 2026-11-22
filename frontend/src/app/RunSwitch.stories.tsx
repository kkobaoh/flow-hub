import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { RunSwitch } from './RunSwitch';

const meta = {
  title: '画面の枠/RunSwitch',
  component: RunSwitch,
  args: { running: true, onStart: () => {}, onStop: () => {} },
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
  return <RunSwitch running={running} onStart={() => setRunning(true)} onStop={() => setRunning(false)} />;
}

/** 押すと確かめる画面が出て、「停止する」「スタートする」で切り替わる。 */
export const Default: Story = {
  render: () => <Live />,
};
