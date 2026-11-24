import { describe, expect, it, vi } from 'vitest';
import { change, click, hover, render } from '../../testing/render';
import { demoAgvs, demoEquipment } from '../../app/demo/demo';
import { ListPanel, type ConsumableHit, type ListPanelProps } from './ListPanel';

const hits: ConsumableHit[] = [
  { id: 'T-0110', variety: '[品種A]', condition: '新品', where: 'ST-1', status: '届けられる', at: { kind: 'equipment', id: 'ST-1' } },
  { id: 'T-0101', variety: '[品種A]', condition: '新品', where: 'DEV-1 のストッカー1・スロット1', status: '使える', at: { kind: 'equipment', id: 'DEV-1' } },
] as ConsumableHit[];

function props(overrides: Partial<ListPanelProps> = {}): ListPanelProps {
  return {
    agvs: demoAgvs,
    equipment: demoEquipment,
    searchConsumables: (q) => hits.filter((h) => h.id.includes(q.toUpperCase())),
    onSelect: () => {},
    onHighlight: () => {},
    onClose: () => {},
    ...overrides,
  };
}

const rows = (c: HTMLElement) => [...c.querySelectorAll('[data-row]')].map((r) => r.getAttribute('data-row'));
const tab = (c: HTMLElement, label: string) => [...c.querySelectorAll('[role="tab"]')].find((t) => t.textContent === label) as HTMLElement;
const search = (c: HTMLElement) => c.querySelector<HTMLInputElement>('input[type="search"]')!;

describe('ListPanel', () => {
  it('AGV・装置・ステーション・消耗品のタブを出し、最初は AGV を番号の順に並べる', () => {
    const { container } = render(<ListPanel {...props()} />);

    expect([...container.querySelectorAll('[role="tab"]')].map((t) => t.textContent)).toEqual(['AGV', '装置', 'ステーション', '消耗品']);
    expect(rows(container)).toEqual(['AGV-1', 'AGV-2', 'AGV-3']);
  });

  it('緊急停止中などの AGV を上に並べる', () => {
    const agvs = demoAgvs.map((a) => (a.id === 'AGV-3' ? { ...a, operation: 'emergencyStopped' as const } : a));
    const { container } = render(<ListPanel {...props({ agvs })} />);

    expect(rows(container)).toEqual(['AGV-3', 'AGV-1', 'AGV-2']);
  });

  it('装置のタブは、気をつけることがあるものを上に並べる', () => {
    const { container } = render(<ListPanel {...props()} />);

    click(tab(container, '装置'));

    expect(rows(container)).toEqual(['DEV-1', 'DEV-2', 'DEV-3']);
    expect(container.textContent).toContain('回収待ち 2、ID 食い違い');
  });

  it('タブごとに、番号や名前で絞り込む。合うものがなければ、そう出す', () => {
    const { container } = render(<ListPanel {...props()} />);

    change(search(container), '2');
    expect(rows(container)).toEqual(['AGV-2']);

    change(search(container), '9');
    expect(rows(container)).toEqual([]);
    expect(container.textContent).toContain('「9」に合うものはありません。番号を確かめてください。');

    click(tab(container, 'ステーション'));
    expect(search(container).value).toBe('');
    expect(rows(container)).toEqual(['ST-1']);
  });

  it('消耗品のタブは、ID を入れたときだけ、合うものを ID の順に並べる', () => {
    const { container } = render(<ListPanel {...props()} />);
    click(tab(container, '消耗品'));

    expect(rows(container)).toEqual([]);
    expect(container.textContent).toContain('消耗品の ID を入れると、ある場所をマップで示します。');

    change(search(container), 't-01');
    expect(rows(container)).toEqual(['T-0101', 'T-0110']);
  });

  it('行を押すと選び、マウスを乗せたことを知らせる', () => {
    const onSelect = vi.fn();
    const onHighlight = vi.fn();
    const { container } = render(<ListPanel {...props({ onSelect, onHighlight })} />);
    const agv2 = container.querySelector('[data-row="AGV-2"]')!;

    hover(agv2, true);
    hover(agv2, false);
    click(agv2);
    click(tab(container, '消耗品'));
    change(search(container), 'T-0101');
    click(container.querySelector('[data-row="T-0101"]')!);

    // 3つめの null は、タブを切り替えたときに、前のタブで乗せていたものを外す知らせ
    expect(onHighlight.mock.calls).toEqual([[{ kind: 'agv', id: 'AGV-2' }], [null], [null]]);
    expect(onSelect.mock.calls).toEqual([[{ kind: 'agv', id: 'AGV-2' }], [{ kind: 'consumable', id: 'T-0101' }]]);
  });
});
