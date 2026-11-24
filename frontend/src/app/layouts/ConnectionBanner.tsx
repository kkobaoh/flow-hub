import type { ConnectionState } from '../realtime/link';

const place = 'fixed inset-x-0 bottom-0 z-40 m-0 px-3 py-2.5 text-center text-sm font-bold text-white shadow-[0_-4px_14px_rgba(35,38,43,0.25)]';

export interface ConnectionBannerProps {
  readonly connection: ConnectionState;
}

/**
 * サーバーとつながっていないときに出す帯。つながっているときは何も出さない。
 * つながるまでは、操作のボタンを押せなくしている（安全側に倒す）ことも伝える。
 * 画面の下に重ねて出す（上の帯やメッセージのタブの位置を、ずらさないため）。確かめる画面（z-50）よりは下にする。
 */
export function ConnectionBanner({ connection }: ConnectionBannerProps) {
  switch (connection) {
    case 'connected':
      return null;
    case 'connecting':
      return (
        <p role="status" className={`${place} bg-graphite`}>
          サーバーとつないでいます。つながるまで、操作はできません。
        </p>
      );
    case 'reconnecting':
      return (
        <p role="alert" className={`${place} bg-estop`}>
          サーバーとつながっていません（つなぎ直しています）。表示は、切れる前の様子です。つながるまで、操作はできません。
        </p>
      );
    default:
      throw new Error(`知らないつながりの様子です: ${JSON.stringify(connection satisfies never)}`);
  }
}
