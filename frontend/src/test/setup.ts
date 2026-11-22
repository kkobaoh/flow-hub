import { afterEach } from 'vitest';
import { cleanup } from './render';

// React に、テストの中で act を使っていることを知らせる
(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

afterEach(() => {
  cleanup();
});
