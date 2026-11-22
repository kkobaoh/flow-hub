import type { Meta, StoryObj } from '@storybook/react-vite';
import { MapCanvas } from './MapCanvas';
import { heading, panelId } from './model';
import { sampleLayout } from './sampleLayout';

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

/** デザインの試作と同じ並び。 */
export const Sample: Story = {
  args: { layout: sampleLayout },
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
