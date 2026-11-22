import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { MessageRail, MessageTab, type Message } from './MessageRail';

const sample: Message[] = [
  { id: 'm6', time: '10:42', title: '予約が取れずに止まっている', detail: 'AGV-1 が、パネル P4 へ向かう予約を待っています。', unread: true },
  { id: 'm5', time: '10:31', title: '行ける充電ポートがない', detail: 'AGV-2 は、充電ポートが空いたら向かいます。', unread: true },
  { id: 'm4', time: '10:05', title: '受け渡しの ID が食い違った', detail: 'DEV-1 で、搬送 #11 の受け渡しの ID が合いませんでした。', unread: true },
  { id: 'm3', time: '09:12', title: '補充を頼んだ', detail: '[品種A] を 2個、上位システムに頼みました。', unread: false },
];

const meta = {
  title: '画面の枠/MessageRail',
  component: MessageRail,
  args: { messages: sample, onMarkAllSeen: () => {}, onClose: () => {} },
  decorators: [
    (Story) => (
      <div className="w-[300px] bg-ground">
        <Story />
      </div>
    ),
  ],
} satisfies Meta<typeof MessageRail>;

export default meta;

type Story = StoryObj<typeof meta>;

function Live() {
  const [messages, setMessages] = useState(sample);
  const [open, setOpen] = useState(true);
  return (
    <div className="flex items-start">
      <MessageTab open={open} unreadCount={messages.filter((m) => m.unread).length} onToggle={() => setOpen((v) => !v)} />
      {open && (
        <div className="w-[300px]">
          <MessageRail
            messages={messages}
            onMarkAllSeen={() => setMessages((ms) => ms.map((m) => ({ ...m, unread: false })))}
            onClose={() => setOpen(false)}
          />
        </div>
      )}
    </div>
  );
}

/** タブで開け閉めし、「すべて見たことにする」で件数が0になる。 */
export const WithTab: Story = {
  render: () => <Live />,
};

/** メッセージがないとき。 */
export const Empty: Story = {
  args: { messages: [] },
};
