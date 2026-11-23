import { afterEach, describe, expect, it, vi } from 'vitest';
import { postCommand, retryDelayMs } from './link';

describe('retryDelayMs', () => {
  it('すぐ、2秒、5秒と間を空け、そのあとは10秒ごとに、ずっとつなぎ直す', () => {
    expect([0, 1, 2, 3, 4, 100].map(retryDelayMs)).toEqual([0, 2000, 5000, 10000, 10000, 10000]);
  });
});

describe('postCommand', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('POST で送り、受け付けられたら ok', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetch);

    await expect(postCommand('http://api', '/system/start', 10_000)).resolves.toBe('ok');
    expect(fetch).toHaveBeenCalledWith('http://api/system/start', expect.objectContaining({ method: 'POST' }));
  });

  it('サーバーが断ったら failed', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 500 })));

    await expect(postCommand('', '/system/stop', 10_000)).resolves.toBe('failed');
  });

  it('つながらないときや、時間内に応答がないときは failed にして、やり直さない', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {});
    const fetch = vi.fn().mockRejectedValue(new DOMException('時間切れ', 'TimeoutError'));
    vi.stubGlobal('fetch', fetch);

    await expect(postCommand('', '/system/stop', 10_000)).resolves.toBe('failed');
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it('決めた時間であきらめるように、送るときに時間切れの合図を付ける', async () => {
    const timeout = vi.spyOn(AbortSignal, 'timeout');
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })));

    await postCommand('', '/system/start', 10_000);

    expect(timeout).toHaveBeenCalledWith(10_000);
  });
});
