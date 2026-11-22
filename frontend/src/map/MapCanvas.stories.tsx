import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { agvId, type AgvId, type AgvOnMap } from './agv';
import { MapCanvas } from './MapCanvas';
import { heading, panelId } from './model';
import { sampleEquipment, sampleLayout } from './sampleLayout';

const meta = {
  title: 'マップ/MapCanvas',
  component: MapCanvas,
  decorators: [
    (Story) => (
      <div className="h-[480px] bg-floor p-6">
        <Story />
      </div>
    ),
  ],
} satisfies Meta<typeof MapCanvas>;

export default meta;

type Story = StoryObj<typeof meta>;

const sampleAgvs: AgvOnMap[] = [
  { id: agvId('AGV-1'), label: '1', panel: panelId('P3'), heading: heading(90), status: { activity: 'moving', charging: false, carrierMoving: false } },
  { id: agvId('AGV-2'), label: '2', panel: panelId('SB2'), heading: heading(90), status: { activity: 'idle', charging: true, carrierMoving: false } },
  { id: agvId('AGV-3'), label: '3', panel: panelId('SB'), heading: heading(90), status: { activity: 'idle', charging: false, carrierMoving: false } },
];

const sampleRoute = [panelId('P3'), panelId('P4'), panelId('P5'), panelId('P6'), panelId('D1')];

/** デザインの試作と同じ並び。 */
export const Sample: Story = {
  args: { layout: sampleLayout },
};

/** AGV、予約しているパネル、走る予定の経路。 */
export const WithAgvs: Story = {
  args: {
    layout: sampleLayout,
    agvs: sampleAgvs,
    reservedPanels: [panelId('P3'), panelId('P4')],
    routes: [sampleRoute],
    equipment: sampleEquipment,
  },
};

/** 標示灯の4つの色（緑と紫は、キャリアを上げ下げしているあいだ点滅する）。 */
export const IndicatorLights: Story = {
  args: {
    layout: sampleLayout,
    agvs: [
      { id: agvId('AGV-1'), label: '1', panel: panelId('P2'), heading: heading(90), status: { activity: 'moving', charging: false, carrierMoving: false } },
      { id: agvId('AGV-2'), label: '2', panel: panelId('P7'), heading: heading(270), status: { activity: 'idle', charging: false, carrierMoving: false } },
      { id: agvId('AGV-3'), label: '3', panel: panelId('SB2'), heading: heading(90), status: { activity: 'idle', charging: true, carrierMoving: true } },
      { id: agvId('AGV-4'), label: '4', panel: panelId('P5'), heading: heading(180), status: { activity: 'idle', charging: false, carrierMoving: false } },
    ],
  },
};

function SelectableMap() {
  const [selected, setSelected] = useState<AgvId | null>(null);
  const [highlighted, setHighlighted] = useState<AgvId | null>(null);
  return (
    <MapCanvas
      layout={sampleLayout}
      agvs={sampleAgvs}
      selectedAgv={selected}
      highlightedAgv={highlighted}
      onSelectAgv={setSelected}
      onHighlightAgv={setHighlighted}
    />
  );
}

/** AGV を押すと選ぶ（水色の実線）。マウスを乗せると水色の点線。 */
export const Selectable: Story = {
  args: { layout: sampleLayout },
  render: () => <SelectableMap />,
};

/** 十字がずれたパネル（ラインを引き直したところ）。つながりは斜めに描く。 */
export const CrossOffset: Story = {
  args: {
    layout: {
      panelSizeMm: 1000,
      panels: [
        { id: panelId('A'), type: 'Normal', hasChargingPin: false, column: 0, row: 0, crossOffsetMm: { x: 0, y: 0 } },
        { id: panelId('B'), type: 'Normal', hasChargingPin: false, column: 1, row: 0, crossOffsetMm: { x: 0, y: 180 } },
        { id: panelId('C'), type: 'Standby', hasChargingPin: true, column: 2, row: 0, crossOffsetMm: { x: 0, y: 180 } },
      ],
      connections: [
        { from: panelId('A'), to: panelId('B'), distanceMm: 1016, heading: heading(100), oneWay: false },
        { from: panelId('B'), to: panelId('C'), distanceMm: 1000, heading: heading(90), oneWay: false },
      ],
    },
  },
};
