import { afterEach, expect, it, vi } from 'vitest';

afterEach(() => { vi.unstubAllGlobals(); vi.resetModules(); });

it('未認証なら一度だけ終了し、その後の業務要求を送らない', async () => {
  const { watchScreenSession, screenFetch, observeScreenStatus } = await import('./screenSession');
  const stop = vi.fn();
  watchScreenSession(stop);
  const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 401 }));
  vi.stubGlobal('fetch', fetch);
  await screenFetch('/system/status');
  observeScreenStatus(401);
  expect(stop).toHaveBeenCalledTimes(1);
  await expect(screenFetch('/system/start')).rejects.toThrow();
  expect(fetch).toHaveBeenCalledTimes(1);
});

it('通信切断・権限不足・サーバー障害ではセッションを終了しない', async () => {
  const { watchScreenSession, screenFetch } = await import('./screenSession');
  const stop = vi.fn();
  watchScreenSession(stop);
  vi.stubGlobal('fetch', vi.fn().mockRejectedValueOnce(new TypeError('offline'))
    .mockResolvedValueOnce(new Response(null, { status: 403 })).mockResolvedValueOnce(new Response(null, { status: 503 })));
  await expect(screenFetch('/system/status')).rejects.toThrow('offline');
  expect((await screenFetch('/system/status')).status).toBe(403);
  expect((await screenFetch('/system/status')).status).toBe(503);
  expect(stop).not.toHaveBeenCalled();
});
