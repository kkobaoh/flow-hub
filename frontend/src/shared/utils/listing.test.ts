import { describe, expect, it } from 'vitest';
import { byIdNumber, matchesQuery, sortForList } from './listing';

describe('byIdNumber（番号の順）', () => {
  it('番号は数として比べる（AGV-2 は AGV-10 より前）', () => {
    expect(['AGV-10', 'AGV-2', 'AGV-1'].sort(byIdNumber)).toEqual(['AGV-1', 'AGV-2', 'AGV-10']);
  });
});

describe('sortForList（気をつけるものを上、残りは番号の順）', () => {
  it('気をつけるものを上にし、それぞれの中は番号の順にする', () => {
    const items = [
      { id: 'AGV-10', watch: false },
      { id: 'AGV-3', watch: true },
      { id: 'AGV-2', watch: false },
      { id: 'AGV-11', watch: true },
    ];

    expect(sortForList(items, (i) => i.watch).map((i) => i.id)).toEqual(['AGV-3', 'AGV-11', 'AGV-2', 'AGV-10']);
  });
});

describe('matchesQuery（探す）', () => {
  it('大文字・小文字、区切りの「-」と空白を気にせず、含んでいれば合うとする', () => {
    expect(matchesQuery('AGV-2', '2')).toBe(true);
    expect(matchesQuery('AGV-2', 'agv2')).toBe(true);
    expect(matchesQuery('AGV-2', 'AGV-2')).toBe(true);
    expect(matchesQuery('AGV-2', '3')).toBe(false);
  });

  it('何も入れていなければ、すべてに合う', () => {
    expect(matchesQuery('AGV-2', '')).toBe(true);
    expect(matchesQuery('AGV-2', '  ')).toBe(true);
  });
});
