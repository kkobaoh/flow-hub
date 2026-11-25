import type { Meta, StoryObj } from '@storybook/react-vite';
import { App } from './App';
import { createFakeLink } from '../testing/fakeLink';
import type { AgvsSnapshot } from '../features/fleet/api/agvSnapshots';
import type { ServerLink } from './realtime/link';

/** Storybook で見せる AGV の様子（サーバーから届く形）。 */
const storyAgvs: AgvsSnapshot = {
  version: 1,
  agvs: [
    {
      id: 'AGV-1', panel: 'P3', heading: 90, state: 'moving', control: 'auto', carrierMoving: false, batteryPercent: 72,
      reservedPanels: ['P3', 'P4'], route: ['P3', 'P4', 'P5', 'P6', 'D1'],
      transport: { id: 12, from: 'ST-1', to: 'DEV-1', stage: 'movingToDestination', paused: false },
    },
    { id: 'AGV-2', panel: 'SB2', heading: 90, state: 'idle', control: 'assignmentPaused', carrierMoving: false, batteryPercent: 35, reservedPanels: [], route: [], transport: null },
    { id: 'AGV-3', panel: null, heading: null, state: 'unregistered', control: 'detached', carrierMoving: false, batteryPercent: null, reservedPanels: [], route: [], transport: null },
  ],
};

/** Storybook で使う、つながったふりをするつながり。運転の切り替えを送ると、その様子が届く。 */
function storyLink(): ServerLink {
  const fake = createFakeLink({ connection: 'connected', status: { version: 1, state: 'started' }, agvs: storyAgvs });
  let version = 1;
  const answer = (state: 'started' | 'stopped') => {
    version += 1;
    setTimeout(() => fake.emitSystemStatus({ version, state }), 300);
    return Promise.resolve('ok' as const);
  };
  return { connect: fake.connect, startSystem: () => answer('started'), stopSystem: () => answer('stopped'), operateAgv: fake.operateAgv, operateConsumables: fake.operateConsumables, cancelTransport: fake.cancelTransport, operateAgvTypes: fake.operateAgvTypes, operateAgvRegistrations: fake.operateAgvRegistrations, operateConfiguration: fake.operateConfiguration, loadMapDraft: fake.loadMapDraft, saveMapDraft: fake.saveMapDraft, validateMapDraft: fake.validateMapDraft, publishMapDraft: fake.publishMapDraft };
}

const meta = {
  title: 'App',
  component: App,
  args: { link: storyLink() },
} satisfies Meta<typeof App>;

export default meta;

type Story = StoryObj<typeof meta>;

/** つながっている。運転の切り替えを押すと、少しあとに様子が届いて切り替わる。 */
export const Default: Story = {};

/** サーバーとのつながりが切れて、つなぎ直している。 */
export const Disconnected: Story = {
  args: { link: createFakeLink({ connection: 'reconnecting', status: { version: 1, state: 'started' }, agvs: storyAgvs }) },
};
