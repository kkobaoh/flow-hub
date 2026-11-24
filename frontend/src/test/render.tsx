import { act, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';

/** テストで描いた部品。テストが終わったら、setup.ts が片付ける。 */
export interface Rendered {
  readonly container: HTMLElement;
  rerender(node: ReactNode): void;
}

const mounted: { root: Root; container: HTMLElement }[] = [];

/** 部品を jsdom の中に描く（React の act で、描き終わるまで待つ）。 */
export function render(node: ReactNode): Rendered {
  const container = document.createElement('div');
  document.body.append(container);
  const root = createRoot(container);
  act(() => root.render(node));
  mounted.push({ root, container });
  return {
    container,
    rerender: (next) => act(() => root.render(next)),
  };
}

export function cleanup(): void {
  for (const { root, container } of mounted.splice(0)) {
    act(() => root.unmount());
    container.remove();
  }
}

export function click(element: Element): void {
  act(() => {
    element.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  });
}

export function pressKey(element: Element, key: string): void {
  act(() => {
    element.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
  });
}

/** ポインター（マウスや指）の知らせを送る。jsdom に PointerEvent がなければ、MouseEvent に pointerId を足して送る。 */
export function pointer(
  element: Element,
  type: 'pointerdown' | 'pointermove' | 'pointerup' | 'pointercancel',
  at: { x: number; y: number },
  pointerId = 1,
): void {
  act(() => {
    const init = { bubbles: true, cancelable: true, clientX: at.x, clientY: at.y, pointerId };
    const event = typeof PointerEvent === 'function' ? new PointerEvent(type, init) : new MouseEvent(type, init);
    if (!('pointerId' in event) || event.pointerId !== pointerId) {
      Object.defineProperty(event, 'pointerId', { value: pointerId });
    }
    element.dispatchEvent(event);
  });
}

export function wheel(element: Element, deltaY: number, at: { x: number; y: number }): void {
  act(() => {
    element.dispatchEvent(new WheelEvent('wheel', { bubbles: true, cancelable: true, deltaY, clientX: at.x, clientY: at.y }));
  });
}

/** 入力欄や選択欄の値を変える（React が変化に気づくように、ブラウザーと同じ手順で値を入れて知らせる）。 */
export function change(element: HTMLInputElement | HTMLSelectElement, value: string): void {
  act(() => {
    const proto = element instanceof HTMLSelectElement ? HTMLSelectElement.prototype : HTMLInputElement.prototype;
    Object.getOwnPropertyDescriptor(proto, 'value')?.set?.call(element, value);
    element.dispatchEvent(new Event(element instanceof HTMLSelectElement ? 'change' : 'input', { bubbles: true }));
  });
}

export function hover(element: Element, on: boolean): void {
  act(() => {
    element.dispatchEvent(new MouseEvent(on ? 'mouseover' : 'mouseout', { bubbles: true }));
  });
}
