import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { agvId, type AgvId, type AgvOnMap } from './agv';
import { MapViewport } from './MapViewport';
import { heading, panelId } from './model';
import { sampleLayout } from './sampleLayout';

const meta = {
  title: 'マップ/MapViewport',
  component: MapViewport,
  decorators: [
    (Story) => (
      <div className="h-[560px] bg-floor">
        <Story />
      </div>
    ),
  ],
} satisfies Meta<typeof MapViewport>;

export default meta;

type Story = StoryObj<typeof meta>;

const agvs: AgvOnMap[] = [
  { id: agvId('AGV-1'), label: '1', panel: panelId('P3'), heading: heading(90), status: { activity: 'moving', charging: false, carrierMoving: false } },
  { id: agvId('AGV-2'), label: '2', panel: panelId('SB2'), heading: heading(90), status: { activity: 'idle', charging: true, carrierMoving: false } },
  { id: agvId('AGV-3'), label: '3', panel: panelId('SB'), heading: heading(90), status: { activity: 'idle', charging: false, carrierMoving: false } },
];

function Interactive() {
  const [selected, setSelected] = useState<AgvId | null>(null);
  const [highlighted, setHighlighted] = useState<AgvId | null>(null);
  return (
    <MapViewport
      layout={sampleLayout}
      agvs={agvs}
      reservedPanels={[panelId('P3'), panelId('P4')]}
      routes={[[panelId('P3'), panelId('P4'), panelId('P5'), panelId('P6'), panelId('D1')]]}
      selectedAgv={selected}
      highlightedAgv={highlighted}
      onSelectAgv={setSelected}
      onHighlightAgv={setHighlighted}
    />
  );
}

/** ドラッグで動かし、ホイール・つまむ操作・右のボタンで拡大・縮小する。AGV を押すと選ぶ。 */
export const Default: Story = {
  args: { layout: sampleLayout },
  render: () => <Interactive />,
};
