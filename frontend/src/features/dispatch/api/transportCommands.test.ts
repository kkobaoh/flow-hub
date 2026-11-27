import { afterEach, describe, expect, it, vi } from 'vitest';
import { cancelNotice, sendCancelTransport } from './transportCommands';

describe('sendCancelTransport', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('搬送の取りやめの窓口へ送り、サーバーが返した結果を返す', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(Response.json({ requestToken: 'csrf' }))
      .mockResolvedValueOnce(Response.json({ result: 'canceling' }));
    vi.stubGlobal('fetch', fetch);

    await expect(sendCancelTransport('http://api', 12, 10_000)).resolves.toBe('canceling');
    expect(fetch).toHaveBeenCalledWith('http://api/transports/12/cancel', expect.objectContaining({
      method: 'POST', headers: expect.objectContaining({ 'X-FlowHub-CSRF': 'csrf' }),
    }));
  });

  it('知らない搬送（404）や応答がないときは failed（やり直さない）', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {});
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(Response.json({ requestToken: 'csrf' })).mockResolvedValueOnce(new Response(null, { status: 404 })));

    await expect(sendCancelTransport('', 12, 10_000)).resolves.toBe('failed');
  });
  it('取りやめの権限が失われた場合は理由を表示する', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(Response.json({ requestToken: 'csrf' })).mockResolvedValueOnce(new Response(null, { status: 403 })));
    expect(cancelNotice(await sendCancelTransport('', 12, 10_000))).toBe('搬送を取りやめる権限がありません。');
  });
});

describe('cancelNotice（取りやめの結果の知らせ）', () => {
  it.each([
    ['canceled', null],
    ['canceling', '取りやめを受け付けました。受け渡しの途中なので、キャリアを上げたところで止まります。'],
    ['completing', '搬送先で受け渡しを始めていたので、取りやめずに最後まで終えて完了にします。'],
    ['notCancelable', '中断したか、もう終わった搬送なので、取りやめられませんでした。'],
    ['failed', 'サーバーから応答がなく、届いたかわかりません。表示を確かめてください。'],
  ] as const)('%s', (result, notice) => {
    expect(cancelNotice(result)).toBe(notice);
  });
});
