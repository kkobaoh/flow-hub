import { postOperation } from '../../../shared/api/postCommand';

/**
 * 搬送を取りやめた結果（サーバーの TransportCancelResult と同じ言葉）。
 * - canceled：取りやめた。canceling：受け渡しの途中なので、キャリアを上げたところで止まる。
 * - completing：搬送先で受け渡しを始めていたので、最後まで終えて完了にする。notCancelable：中断した、または終えた搬送。
 * - failed：応答がない、知らない搬送、または思わない応答（届いたかわからない。やり直さない）。
 */
export type TransportCancelResult = 'canceled' | 'canceling' | 'completing' | 'notCancelable' | 'failed';

const SERVER_RESULTS: ReadonlySet<Exclude<TransportCancelResult, 'failed'>> = new Set(['canceled', 'canceling', 'completing', 'notCancelable'] as const);

/** 搬送の取りやめを送る。timeoutMs を過ぎても応答がなければ、あきらめて failed にする（やり直さない）。 */
export async function sendCancelTransport(baseUrl: string, number: number, timeoutMs: number): Promise<TransportCancelResult> {
  const result = await postOperation(baseUrl, `/transports/${number}/cancel`, {}, timeoutMs, SERVER_RESULTS);
  // 取りやめの窓口は 409 を返さないので、返ってきたら思わない応答として扱う
  return result === 'refused' ? 'failed' : result;
}

/** 画面に出す、取りやめの結果の知らせ（取りやめたときは null）。 */
export function cancelNotice(result: TransportCancelResult): string | null {
  switch (result) {
    case 'canceled':
      return null;
    case 'canceling':
      return '取りやめを受け付けました。受け渡しの途中なので、キャリアを上げたところで止まります。';
    case 'completing':
      return '搬送先で受け渡しを始めていたので、取りやめずに最後まで終えて完了にします。';
    case 'notCancelable':
      return '中断したか、もう終わった搬送なので、取りやめられませんでした。';
    case 'failed':
      return 'サーバーから応答がなく、届いたかわかりません。表示を確かめてください。';
    default:
      throw new Error(`知らない結果です: ${JSON.stringify(result satisfies never)}`);
  }
}
