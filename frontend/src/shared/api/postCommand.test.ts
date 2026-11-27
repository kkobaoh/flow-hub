import { afterEach, describe, expect, it, vi } from 'vitest';
import { postCommand } from './postCommand';

describe('postCommand', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('POST で送り、受け付けられたら ok', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(Response.json({ requestToken: 'test-token' })).mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetch);

    await expect(postCommand('http://api', '/system/start', 10_000)).resolves.toBe('ok');
    expect(fetch).toHaveBeenCalledWith('http://api/system/start', expect.objectContaining({
      method: 'POST', headers: { 'X-FlowHub-CSRF': 'test-token' },
    }));
  });

  it('サーバーが断ったら failed', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(Response.json({ requestToken: 'token' }))
      .mockResolvedValueOnce(new Response(null, { status: 403 })));

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
    const fetch = vi.fn().mockResolvedValueOnce(Response.json({ requestToken: 'token' })).mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetch);

    await postCommand('', '/system/start', 10_000);

    expect(timeout).toHaveBeenCalledWith(10_000);
    expect(fetch.mock.calls[1]![1].signal).toBe(fetch.mock.calls[0]![1].signal);
  });
});
