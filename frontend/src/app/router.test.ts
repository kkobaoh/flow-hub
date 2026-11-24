import { describe, expect, it } from 'vitest';
import { hashOf, routeFromHash } from './router';

describe('routeFromHash（URL の # のあとから、画面を決める）', () => {
  it('画面の名前を読む', () => {
    expect(routeFromHash('#/transports')).toEqual({ screen: 'transports', target: null });
    expect(routeFromHash('#/users')).toEqual({ screen: 'users', target: null });
  });

  it('画面の名前のあとに、開くもの（装置など）を読む', () => {
    expect(routeFromHash('#/devices/DEV-2')).toEqual({ screen: 'devices', target: 'DEV-2' });
    expect(routeFromHash('#/devices/')).toEqual({ screen: 'devices', target: null });
  });

  it('何もないときや、知らない名前のときは、マップの画面にする', () => {
    expect(routeFromHash('')).toEqual({ screen: 'map', target: null });
    expect(routeFromHash('#/')).toEqual({ screen: 'map', target: null });
    expect(routeFromHash('#/nothing/DEV-1')).toEqual({ screen: 'map', target: null });
  });
});

describe('hashOf', () => {
  it('画面と開くものを、URL の # のあとの形にする', () => {
    expect(hashOf({ screen: 'devices', target: null })).toBe('#/devices');
    expect(hashOf({ screen: 'devices', target: 'DEV-2' })).toBe('#/devices/DEV-2');
  });

  it('読んで戻すと同じになる（記号を含むものも）', () => {
    const route = { screen: 'station', target: 'ST 1/A' } as const;
    expect(routeFromHash(hashOf(route))).toEqual(route);
  });
});
