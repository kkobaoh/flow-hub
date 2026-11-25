import { afterEach, describe, expect, it, vi } from 'vitest';
import { cancelNotice, sendCancelTransport } from './transportCommands';

describe('sendCancelTransport', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('搬送の取りやめの窓口へ送り、サーバーが返した結果を返す', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ result: 'canceling' }), { status: 200 }));
    vi.stubGlobal('fetch', fetch);

    await expect(sendCancelTransport('http://api', 12, 10_000)).resolves.toBe('canceling');
    expect(fetch).toHaveBeenCalledWith('http://api/transports/12/cancel', expect.objectContaining({ method: 'POST' }));
  });

  it('知らない搬送（404）や応答がないときは failed（やり直さない）', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {});
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 404 })));

    await expect(sendCancelTransport('', 12, 10_000)).resolves.toBe('failed');
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
