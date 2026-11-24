import { describe, expect, it } from 'vitest';
import { attentionNote, attentionTag, equipmentId, labelSide, textWidthMm } from './equipment';

describe('equipmentId', () => {
  it('空でない文字列から作れる', () => {
    expect(equipmentId('DEV-1')).toBe('DEV-1');
    expect(() => equipmentId(' ')).toThrow(RangeError);
  });
});

describe('attentionTag（マップに出す短い札）', () => {
  it('気をつけることの種類と数から、札の文字を決める', () => {
    expect(attentionTag({ kind: 'shortage', count: 1 })).toBe('不足 1');
    expect(attentionTag({ kind: 'collectWaiting', count: 2 })).toBe('回収待ち 2');
    expect(attentionTag({ kind: 'idMismatch', count: 1 })).toBe('ID 食い違い');
    expect(attentionTag({ kind: 'setupError', count: 1 })).toBe('段取り替えのエラー');
    expect(attentionTag({ kind: 'needsCheck', count: 1 })).toBe('要確認 1');
    expect(attentionTag({ kind: 'replenishWaiting', count: 2 })).toBe('補充待ち 2');
  });
});

describe('attentionNote（カードに出す見出し）', () => {
  it('数があるものは「〜が n個」にする', () => {
    expect(attentionNote({ kind: 'collectWaiting', count: 2 })).toBe('回収待ちが 2個');
    expect(attentionNote({ kind: 'needsCheck', count: 1 })).toBe('要確認の消耗品が 1個');
    expect(attentionNote({ kind: 'idMismatch', count: 1 })).toBe('ID が食い違った');
  });
});

describe('labelSide（名前の札を出す側）', () => {
  const panel = { x: 2000, y: 0, width: 1000, height: 1000 };

  it('外形のうち、受け渡しパネルからはみ出したところが広い側に出す', () => {
    expect(labelSide({ x: 1500, y: -750, width: 2000, height: 1650 }, panel)).toBe('top');
    expect(labelSide({ x: 1500, y: -100, width: 2000, height: 1800 }, panel)).toBe('bottom');
  });
});

describe('textWidthMm（札の幅の目安）', () => {
  it('全角の文字は半角より広く数える', () => {
    expect(textWidthMm('ああ', 100)).toBeGreaterThan(textWidthMm('AA', 100));
    expect(textWidthMm('', 100)).toBe(0);
  });
});
