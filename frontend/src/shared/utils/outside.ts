/**
 * 押したところが、selector に合う範囲の中か。押した瞬間の道筋（composedPath）で見るので、
 * 押したものが、document に届くまでに画面から外されても（一覧の行を押すとカードに替わるときなど）、正しく見分けられる。
 */
export function clickedInside(event: Event, selector: string): boolean {
  return event.composedPath().some((node) => node instanceof Element && node.matches(selector));
}
