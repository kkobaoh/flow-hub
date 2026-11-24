import { describe, expect, it } from 'vitest';
import { dateTimeOf } from './dateTime';

describe('dateTimeOf', () => {
  it('「11/20 14:02」の形にする', () => {
    expect(dateTimeOf(new Date('2026-11-20T14:02:00'))).toBe('11/20 14:02');
  });
});
