import { describe, expect, it, vi } from 'vitest';
import { agvId } from '../map/agv';
import { change, click, render } from '../test/render';
import type { AgvEntry, AgvType } from './agvs';
import { AgvsScreen, type AgvsScreenProps } from './AgvsScreen';
import { initialRoles } from './permissions';

const entries: AgvEntry[] = [
  { id: agvId('AGV-1'), type: '[種類A]', carrier: '[キャリアA]', control: 'auto', registration: 'both' },
  { id: agvId('AGV-2'), type: '[種類A]', carrier: '[キャリアA]', control: 'detached', registration: 'both' },
  { id: agvId('AGV-4'), type: '[種類A]', carrier: null, control: null, registration: 'trafficOnly' },
];

const types: AgvType[] = [
  { name: '[種類A]', forward: 1000, backward: 500, rotation: 45, chargeAt: 20, resumeAt: 80, special: false },
  { name: '[種類B]', forward: 800, backward: 400, rotation: 30, chargeAt: 25, resumeAt: 85, special: true },
];

function setup(overrides: Partial<AgvsScreenProps> = {}) {
  const handlers = {
    onAddAgv: vi.fn(),
    onChangeAgv: vi.fn(),
    onAlignAgv: vi.fn(),
    onDeleteAgv: vi.fn(),
    onSaveType: vi.fn(),
    onDeleteType: vi.fn(),
  };
  const view = render(
    <AgvsScreen entries={entries} types={types} carriers={['[キャリアA]', '[キャリアB]']} defaults={{ chargeAt: 20, resumeAt: 80 }} permissions={initialRoles.管理者} {...handlers} {...overrides} />,
  );
  return { ...view, ...handlers };
}

const byText = (root: Element, text: string) => [...root.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent === text)!;
const agvRow = (c: HTMLElement, id: string) => [...c.querySelectorAll('[aria-labelledby="agv-title"] tbody tr')].find((r) => r.querySelector('td')?.textContent === id)!;
const typeRow = (c: HTMLElement, name: string) => [...c.querySelectorAll('[aria-labelledby="type-title"] tbody tr')].find((r) => r.querySelector('td')?.textContent === name)!;
const dialog = (c: HTMLElement) => c.querySelector('[role="dialog"]')!;

describe('AgvsScreen（AGV と AGV の種類の登録）', () => {
  it('登録が食い違っている AGV を上にし、番号の順に並べる', () => {
    const { container } = setup();

    expect([...container.querySelectorAll('[aria-labelledby="agv-title"] tbody tr')].map((r) => r.querySelector('td')?.textContent)).toEqual(['AGV-4', 'AGV-1', 'AGV-2']);
    expect(agvRow(container, 'AGV-4').textContent).toContain('走行の登録だけ（配車に未登録）');
  });

  it('AGV を加える：番号・種類・キャリアの種類を入れて保存する', () => {
    const { container, onAddAgv } = setup();

    click(byText(container, 'AGV を加える'));
    expect(byText(dialog(container), 'AGV を保存する').disabled).toBe(true);
    change(dialog(container).querySelector<HTMLInputElement>('input[aria-label="AGV の番号"]')!, 'AGV-5');
    change(dialog(container).querySelector<HTMLSelectElement>('select[aria-label="AGV の種類"]')!, '[種類B]');
    change(dialog(container).querySelector<HTMLSelectElement>('select[aria-label="キャリアの種類"]')!, '[キャリアB]');
    click(byText(dialog(container), 'AGV を保存する'));

    expect(onAddAgv).toHaveBeenCalledWith({ id: 'AGV-5', type: '[種類B]', carrier: '[キャリアB]' });
  });

  it('切り離しでない AGV は、変えたり消したりできず、理由を出す', () => {
    const { container } = setup();

    expect(byText(agvRow(container, 'AGV-1'), '変える').disabled).toBe(true);
    expect(agvRow(container, 'AGV-1').textContent).toContain('切り離しのときだけ');
    expect(byText(agvRow(container, 'AGV-2'), '変える').disabled).toBe(false);
  });

  it('AGV を消すのは、確かめる画面で選んだときだけ', () => {
    const { container, onDeleteAgv } = setup();

    click(byText(agvRow(container, 'AGV-2'), '消す'));
    click(byText(container.querySelector('[role="alertdialog"]')!, 'AGV を消す'));

    expect(onDeleteAgv).toHaveBeenCalledWith('AGV-2');
  });

  it('登録が食い違っている AGV は、足りない側の項目を入れてそろえる', () => {
    const { container, onAlignAgv } = setup();

    click(byText(agvRow(container, 'AGV-4'), '登録をそろえる'));
    expect(dialog(container).querySelector('select[aria-label="AGV の種類"]')).toBeNull();
    change(dialog(container).querySelector<HTMLSelectElement>('select[aria-label="キャリアの種類"]')!, '[キャリアA]');
    click(byText(dialog(container), 'AGV を保存する'));

    expect(onAlignAgv).toHaveBeenCalledWith('AGV-4', { type: '[種類A]', carrier: '[キャリアA]' });
  });

  it('種類を加える：充電の値は空なら既定値で埋める', () => {
    const { container, onSaveType } = setup();

    click(byText(container, '種類を加える'));
    const d = dialog(container);
    change(d.querySelector<HTMLInputElement>('input[aria-label="種類の名前"]')!, '[種類C]');
    change(d.querySelector<HTMLInputElement>('input[aria-label="前進の速さ"]')!, '900');
    change(d.querySelector<HTMLInputElement>('input[aria-label="後退の速さ"]')!, '450');
    change(d.querySelector<HTMLInputElement>('input[aria-label="回転の速さ"]')!, '40');
    expect(d.querySelector<HTMLInputElement>('input[aria-label="充電に向かう残量"]')!.placeholder).toBe('既定値 20');
    click(d.querySelector('input[type="checkbox"]')!);
    click(byText(d, '種類を保存する'));

    expect(onSaveType).toHaveBeenCalledWith(null, { name: '[種類C]', forward: 900, backward: 450, rotation: 40, chargeAt: 20, resumeAt: 80, special: true });
  });

  it('使う AGV がある種類は消せず、理由を出す', () => {
    const { container, onDeleteType } = setup();

    expect(byText(typeRow(container, '[種類A]'), '消す').disabled).toBe(true);
    expect(typeRow(container, '[種類A]').textContent).toContain('AGV-1、AGV-2、AGV-4 が使っているので、消せません。');

    click(byText(typeRow(container, '[種類B]'), '消す'));
    click(byText(container.querySelector('[role="alertdialog"]')!, '種類を消す'));
    expect(onDeleteType).toHaveBeenCalledWith('[種類B]');
  });

  it('管理者でなければ、どの操作も使えず、理由を出す', () => {
    const { container } = setup({ permissions: initialRoles.保守 });

    expect(byText(container, 'AGV を加える').disabled).toBe(true);
    expect(byText(container, '種類を加える').disabled).toBe(true);
    expect(container.textContent).toContain('管理者のロールで使えます');
  });
});
