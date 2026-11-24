import { describe, expect, it } from 'vitest';
import { retryDelayMs } from './link';

describe('retryDelayMs', () => {
  it('すぐ、2秒、5秒と間を空け、そのあとは10秒ごとに、ずっとつなぎ直す', () => {
    expect([0, 1, 2, 3, 4, 100].map(retryDelayMs)).toEqual([0, 2000, 5000, 10000, 10000, 10000]);
  });
});
