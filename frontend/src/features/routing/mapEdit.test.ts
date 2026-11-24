import { describe, expect, it } from 'vitest';
import type { Rect } from './drawing';
import { heading, panelId, type MapLayout, type Panel } from './model';
import {
  connect,
  connectionView,
  editBounds,
  isDraftChanged,
  nextPanelId,
  placePanel,
  publishBlockedReason,
  removeConnection,
  removePanel,
  renamePanel,
  setConnection,
  setPanelType,
  type MapDraft,
} from './mapEdit';

const panel = (id: string, column: number, row: number, overrides: Partial<Panel> = {}): Panel => ({
  id: panelId(id),
  type: 'Normal',
  hasChargingPin: false,
  column,
  row,
  crossOffsetMm: { x: 0, y: 0 },
  ...overrides,
});

const layout = (panels: Panel[], connections: MapLayout['connections'] = []): MapLayout => ({ panelSizeMm: 1000, panels, connections });
const draft = (l: MapLayout, outlines: Readonly<Record<string, Rect>> = {}): MapDraft => ({ layout: l, outlines });

describe('nextPanelId（置いたパネルの ID）', () => {
  it('種類ごとの頭文字に、まだ使っていないいちばん小さい番号を付ける', () => {
    const panels = [panel('P1', 0, 0), panel('P3', 1, 0), panel('SP', 2, 0, { type: 'SpecialTransfer' })];
    expect(nextPanelId('Normal', panels)).toBe('P2');
    expect(nextPanelId('Transfer', panels)).toBe('D1');
    expect(nextPanelId('Standby', panels)).toBe('SB1');
    expect(nextPanelId('SpecialTransfer', panels)).toBe('SP1');
  });
});

describe('placePanel と removePanel', () => {
  it('空いた格子にだけ置ける', () => {
    const d = draft(layout([panel('P1', 0, 0)]));

    const placed = placePanel(d, 'Transfer', 1, 0);
    expect(placed.layout.panels.map((p) => [p.id, p.type, p.column, p.row])).toContainEqual(['D1', 'Transfer', 1, 0]);
    expect(placePanel(d, 'Normal', 0, 0)).toBe(d);
  });

  it('パネルを消すと、そのつながりも消す', () => {
    const d = draft(layout([panel('P1', 0, 0), panel('P2', 1, 0)], [{ from: panelId('P1'), to: panelId('P2'), distanceMm: 1000, heading: heading(90), oneWay: false }]));

    const after = removePanel(d, panelId('P2'));
    expect(after.layout.panels.map((p) => p.id)).toEqual(['P1']);
    expect(after.layout.connections).toEqual([]);
  });
});

describe('connect（つなぐ）', () => {
  const d = draft(layout([panel('P1', 0, 0), panel('P2', 1, 0, { crossOffsetMm: { x: 0, y: 1000 } }), panel('P3', 0, 1), panel('P4', 1, 1)]));

  it('上下左右の隣だけつなげ、向きと距離を十字の位置から計算する。一方通行なし', () => {
    const r = connect(d, panelId('P1'), panelId('P3'));
    expect(r.kind).toBe('connected');
    if (r.kind === 'connected') {
      expect(r.draft.layout.connections).toEqual([{ from: 'P1', to: 'P3', distanceMm: 1000, heading: 180, oneWay: false }]);
    }
    const diagonal = connect(d, panelId('P1'), panelId('P2'));
    expect(diagonal.kind === 'connected' && diagonal.draft.layout.connections[0]).toMatchObject({ distanceMm: 1414, heading: 135 });
  });

  it('斜めの隣や、すでにつながっている組はつなげない', () => {
    expect(connect(d, panelId('P1'), panelId('P4'))).toEqual({ kind: 'refused', reason: 'P1 と P4 は、上下左右に隣り合っていないので、つなげません。' });
    const once = connect(d, panelId('P1'), panelId('P3'));
    if (once.kind !== 'connected') {
      throw new Error('つながるはず');
    }
    expect(connect(once.draft, panelId('P3'), panelId('P1'))).toEqual({ kind: 'refused', reason: 'P3 と P1 は、もうつながっています。' });
  });
});

describe('つながりの見え方と直し方', () => {
  const c = { from: panelId('P1'), to: panelId('P2'), distanceMm: 1000, heading: heading(90), oneWay: false };
  const d = draft(layout([panel('P1', 0, 0), panel('P2', 1, 0, { type: 'Standby' })], [c]));

  it('選んだパネルから見た向きにする（逆から見ると 180度反対）', () => {
    expect(connectionView(c, panelId('P1'))).toEqual({ other: 'P2', heading: 90 });
    expect(connectionView(c, panelId('P2'))).toEqual({ other: 'P1', heading: 270 });
  });

  it('選んだパネルから見た向きで直す', () => {
    const after = setConnection(d, c, panelId('P2'), { heading: 260 });
    expect(after.layout.connections[0]).toMatchObject({ from: 'P1', to: 'P2', heading: 80 });
  });

  it('つながりを外す', () => {
    expect(removeConnection(d, c).layout.connections).toEqual([]);
  });
});

describe('renamePanel（ID を直す）', () => {
  const c = { from: panelId('P1'), to: panelId('P2'), distanceMm: 1000, heading: heading(90), oneWay: false };
  const d = draft(layout([panel('P1', 0, 0), panel('P2', 1, 0)], [c]));

  it('つながりの ID も直す', () => {
    const r = renamePanel(d, panelId('P2'), 'P9', []);
    expect(r.kind === 'renamed' && r.draft.layout.connections[0]?.to).toBe('P9');
  });

  it('空や同じ ID、装置やステーションが使っているパネルは直せない', () => {
    expect(renamePanel(d, panelId('P2'), ' ', [])).toEqual({ kind: 'refused', reason: 'パネルの ID を入れてください。' });
    expect(renamePanel(d, panelId('P2'), 'P1', [])).toEqual({ kind: 'refused', reason: 'P1 は、もう使っています。' });
    expect(renamePanel(d, panelId('P2'), 'P9', ['P2'])).toEqual({ kind: 'refused', reason: 'P2 は装置かステーションの受け渡しパネルなので、ID を直せません。' });
  });
});

describe('setPanelType（種類を変える）', () => {
  it('その種類に付けられない充電ピンと下ろす角度は外す', () => {
    const d = draft(layout([panel('D1', 0, 0, { type: 'Transfer', hasChargingPin: true })]));
    const special = setPanelType(d, panelId('D1'), 'SpecialTransfer');
    expect(special.layout.panels[0]).toMatchObject({ type: 'SpecialTransfer', hasChargingPin: false });

    const withAngle = draft(layout([panel('SP', 0, 0, { type: 'SpecialTransfer', carrierLoweringHeading: heading(45) })]));
    expect(setPanelType(withAngle, panelId('SP'), 'Normal').layout.panels[0]?.carrierLoweringHeading).toBeUndefined();
  });
});

describe('editBounds（編集する格子の広さ）', () => {
  it('パネルのまわりに1枚ずつ空けて広げる', () => {
    expect(editBounds([panel('P1', 2, 1), panel('P2', 3, 1)])).toEqual({ minColumn: 1, maxColumn: 4, minRow: 0, maxRow: 2 });
    expect(editBounds([])).toEqual({ minColumn: 0, maxColumn: 5, minRow: 0, maxRow: 3 });
  });
});

describe('isDraftChanged と publishBlockedReason', () => {
  const d = draft(layout([panel('P1', 0, 0)]));

  it('保存した下書きと違うか', () => {
    expect(isDraftChanged(d, draft(layout([panel('P1', 0, 0)])))).toBe(false);
    expect(isDraftChanged(d, placePanel(d, 'Normal', 1, 0))).toBe(true);
  });

  it('公開できるのは、検証が通った下書きで、運転状態が停止、走っている AGV がないとき', () => {
    const ok = { validated: d, problems: [], current: d, running: false, travelingAgvs: [], allowed: true } as const;
    expect(publishBlockedReason(ok)).toBeNull();
    expect(publishBlockedReason({ ...ok, allowed: false })).toBe('マップの編集・公開は、管理者のロールで使えます。');
    expect(publishBlockedReason({ ...ok, validated: null })).toBe('先に「確かめる（検証）」を押してください。');
    expect(publishBlockedReason({ ...ok, current: placePanel(d, 'Normal', 1, 0) })).toBe('検証のあとに変えたところがあります。もう一度確かめてください。');
    expect(publishBlockedReason({ ...ok, problems: ['x'] })).toBe('検証で見つかった問題を直すと、公開できます。');
    expect(publishBlockedReason({ ...ok, running: true })).toBe('運転状態を停止にすると、公開できます。');
    expect(publishBlockedReason({ ...ok, travelingAgvs: ['AGV-1'] })).toBe('AGV-1 が走っているので、止まるまで公開できません。');
  });
});
