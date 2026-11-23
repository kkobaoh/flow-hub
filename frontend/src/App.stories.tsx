import type { Meta, StoryObj } from '@storybook/react-vite';
import { App } from './App';
import { createFakeLink } from './server/fakeLink';
import type { ServerLink } from './server/link';

/** Storybook で使う、つながったふりをするつながり。運転の切り替えを送ると、その様子が届く。 */
function storyLink(): ServerLink {
  const fake = createFakeLink({ connection: 'connected', status: { version: 1, state: 'started' } });
  let version = 1;
  const answer = (state: 'started' | 'stopped') => {
    version += 1;
    setTimeout(() => fake.emitSystemStatus({ version, state }), 300);
    return Promise.resolve('ok' as const);
  };
  return { connect: fake.connect, startSystem: () => answer('started'), stopSystem: () => answer('stopped') };
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
  args: { link: createFakeLink({ connection: 'reconnecting', status: { version: 1, state: 'started' } }) },
};
