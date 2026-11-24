import { describe, expect, it } from 'vitest';
import { heading, panelId } from './model';

describe('panelId', () => {
  it('空でない文字列から作れる', () => {
    expect(panelId('P1')).toBe('P1');
  });

  it('空の文字列からは作れない', () => {
    expect(() => panelId('')).toThrow(RangeError);
    expect(() => panelId('  ')).toThrow(RangeError);
  });
});

describe('heading', () => {
  it('0〜359 の整数から作れる', () => {
    expect(heading(0)).toBe(0);
    expect(heading(359)).toBe(359);
  });

  it('範囲の外や、整数でない値からは作れない', () => {
    expect(() => heading(-1)).toThrow(RangeError);
    expect(() => heading(360)).toThrow(RangeError);
    expect(() => heading(1.5)).toThrow(RangeError);
    expect(() => heading(Number.NaN)).toThrow(RangeError);
  });
});
