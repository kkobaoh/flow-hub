import type { Meta, StoryObj } from '@storybook/react-vite';
import { demoDeviceConfigs, demoHeights, demoMapDraft, demoStationConfigs, demoValidate } from '../app/demo/demo';
import { MapEditPage } from './MapEditPage';
import { initialRoles } from '../features/auth/permissions';

const meta = {
  title: 'マップの編集/MapEditScreen',
  component: MapEditPage,
  args: {
    saved: demoMapDraft,
    draftVersion: 4,
    savedAt: null,
    publishedVersion: 3,
    equipment: [...demoDeviceConfigs, ...demoStationConfigs],
    heights: demoHeights,
    running: false,
    travelingAgvs: [],
    permissions: initialRoles.管理者,
    offline: false,
    onSaveDraft: () => Promise.resolve(null),
    onReload: () => {},
    onValidate: (d) => Promise.resolve(demoValidate(d)),
    onPublish: () => Promise.resolve(null),
    onSaveHeight: () => {},
    onDirtyChange: () => {},
  },
  decorators: [
    (Story) => (
      <div className="flex flex-col gap-4 bg-ground p-5 font-sans text-graphite">
        <Story />
      </div>
    ),
  ],
} satisfies Meta<typeof MapEditPage>;

export default meta;

type Story = StoryObj<typeof meta>;

/** 公開中の版の写しから始める。検証が通れば公開できる。 */
export const Default: Story = {};

/** 運転中は公開できない。 */
export const Running: Story = {
  args: { running: true },
};
