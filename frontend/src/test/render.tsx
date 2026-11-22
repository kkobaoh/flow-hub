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

export function hover(element: Element, on: boolean): void {
  act(() => {
    element.dispatchEvent(new MouseEvent(on ? 'mouseover' : 'mouseout', { bubbles: true }));
  });
}
