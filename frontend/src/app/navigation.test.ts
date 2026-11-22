import { describe, expect, it } from 'vitest';
import { hashOf, screenFromHash } from './navigation';

describe('screenFromHash（URL の # のあとから、画面を決める）', () => {
  it('画面の名前を読む', () => {
    expect(screenFromHash('#/transports')).toBe('transports');
    expect(screenFromHash('#/users')).toBe('users');
  });

  it('何もないときや、知らない名前のときは、マップの画面にする', () => {
    expect(screenFromHash('')).toBe('map');
    expect(screenFromHash('#/')).toBe('map');
    expect(screenFromHash('#/nothing')).toBe('map');
  });
});

describe('hashOf', () => {
  it('画面を、URL の # のあとの形にする', () => {
    expect(hashOf('devices')).toBe('#/devices');
    expect(screenFromHash(hashOf('consumableConfig'))).toBe('consumableConfig');
  });
});
