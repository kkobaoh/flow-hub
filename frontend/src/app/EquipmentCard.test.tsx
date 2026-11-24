import { describe, expect, it, vi } from 'vitest';
import { equipmentId } from '../map/equipment';
import { panelId } from '../map/model';
import { click, render } from '../test/render';
import { EquipmentCard, type EquipmentCardProps, type EquipmentDetail } from './EquipmentCard';
import { initialRoles } from './permissions';

const device: EquipmentDetail = {
  id: equipmentId('DEV-1'),
  kind: 'device',
  panel: panelId('D1'),
  outlineMm: { x: 3050, y: 2900, width: 900, height: 2100 },
  attentions: [{ kind: 'collectWaiting', count: 2 }, { kind: 'idMismatch', count: 1 }],
  details: {
    collectWaiting: 'T-0203（中古品・廃棄）と T-0410（設定にない品種）の回収を頼んでいます。',
    idMismatch: 'ストッカー2・スロット3 の ID が記録と合いませんでした。回収を頼んでいます。',
  },
  stocker: [
    { variety: '[品種A]', target: 2, have: 2 },
    { variety: '[品種D]', target: 0, have: 1 },
  ],
  slots: { total: 6, free: 1, delivering: 1 },
};

const station: EquipmentDetail = {
  id: equipmentId('ST-1'),
  kind: 'station',
  panel: panelId('ST'),
  outlineMm: { x: 1500, y: -750, width: 2000, height: 1650 },
  attentions: [],
  details: {},
  stock: [{ variety: '[品種A]', available: 1, replenishWaiting: 2 }],
};

function props(overrides: Partial<EquipmentCardProps> = {}): EquipmentCardProps {
  return { equipment: device, permissions: initialRoles.運用, onClose: () => {}, onOpen: () => {}, ...overrides };
}

function byText(root: ParentNode, text: string): HTMLButtonElement {
  const found = [...root.querySelectorAll('button')].find((b) => b.textContent === text);
  if (!found) {
    throw new Error(`「${text}」のボタンがありません`);
  }
  return found;
}

describe('EquipmentCard', () => {
  it('見出しに名前と種類と受け渡しパネルを出し、閉じるボタンで閉じる', () => {
    const onClose = vi.fn();
    const { container } = render(<EquipmentCard {...props({ onClose })} />);

    expect(container.querySelector('h2')?.textContent).toBe('DEV-1');
    expect(container.textContent).toContain('装置（受け渡しパネル D1）');
    click(container.querySelector('button[aria-label="閉じる"]')!);
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('気をつけることを、見出しと中身で1件ずつ出す', () => {
    const { container } = render(<EquipmentCard {...props()} />);
    const notes = [...container.querySelectorAll('[data-note]')].map((n) => n.textContent);

    expect(notes).toEqual([
      '回収待ちが 2個T-0203（中古品・廃棄）と T-0410（設定にない品種）の回収を頼んでいます。',
      'ID が食い違ったストッカー2・スロット3 の ID が記録と合いませんでした。回収を頼んでいます。',
    ]);
  });

  it('気をつけることがなければ、そう出す', () => {
    const { container } = render(<EquipmentCard {...props({ equipment: station })} />);

    expect(container.textContent).toContain('気をつけることはありません');
  });

  it('装置は、ストッカーの設定と、スロットの空きを出す', () => {
    const { container } = render(<EquipmentCard {...props()} />);
    const rows = [...container.querySelectorAll('tbody tr')].map((r) => [...r.querySelectorAll('td, th')].map((c) => c.textContent));

    expect(rows).toEqual([
      ['[品種A]', '2', '2'],
      ['[品種D]', '0', '1'],
    ]);
    expect(container.textContent).toContain('スロット 6（空き 1、届ける途中 1）');
  });

  it('ステーションは、品種ごとの数を出す', () => {
    const { container } = render(<EquipmentCard {...props({ equipment: station })} />);
    const rows = [...container.querySelectorAll('tbody tr')].map((r) => [...r.querySelectorAll('td, th')].map((c) => c.textContent));

    expect(container.textContent).toContain('ステーション（受け渡しパネル ST）');
    expect(rows).toEqual([['[品種A]', '1', '2']]);
  });

  it('装置の画面とステーションの画面へ、その装置やステーションを開く', () => {
    const onOpen = vi.fn();
    const deviceCard = render(<EquipmentCard {...props({ onOpen })} />);
    click(byText(deviceCard.container, 'ストッカーの設定を変える（段取り替え）'));
    click(byText(deviceCard.container, '装置とストッカーを開く'));

    const stationCard = render(<EquipmentCard {...props({ onOpen, equipment: station })} />);
    click(byText(stationCard.container, '要確認の消耗品を直す'));
    click(byText(stationCard.container, 'ステーションの在庫を開く'));

    expect(onOpen.mock.calls).toEqual([
      ['devices', 'DEV-1'],
      ['devices', 'DEV-1'],
      ['station', 'ST-1'],
      ['station', 'ST-1'],
    ]);
  });

  it('パーミッションのない操作は押せない（見るための入り口は押せる）', () => {
    const { container } = render(<EquipmentCard {...props({ permissions: initialRoles.閲覧のみ })} />);

    expect(byText(container, 'ストッカーの設定を変える（段取り替え）').disabled).toBe(true);
    expect(byText(container, '装置とストッカーを開く').disabled).toBe(false);
  });
});
