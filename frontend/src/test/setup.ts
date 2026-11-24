import { afterEach } from 'vitest';
import { cleanup } from './render';

// React に、テストの中で act を使っていることを知らせる
(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

afterEach(() => {
  cleanup();
});

// jsdom には ResizeObserver がなく、大きさも測れない。テストでは、見たものを決まった大きさ（1048 x 648）として知らせる。
if (typeof globalThis.ResizeObserver === 'undefined') {
  class FixedSizeObserver {
    constructor(private readonly callback: ResizeObserverCallback) {}
    observe(target: Element): void {
      const contentRect = { width: 1048, height: 648 } as DOMRectReadOnly;
      this.callback([{ target, contentRect } as ResizeObserverEntry], this as unknown as ResizeObserver);
    }
    unobserve(): void {}
    disconnect(): void {}
  }
  globalThis.ResizeObserver = FixedSizeObserver as unknown as typeof ResizeObserver;
}
