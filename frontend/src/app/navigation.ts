import { useEffect, useState } from 'react';
import { screens, type ScreenKey } from './screens';

const KEYS: ReadonlySet<string> = new Set(screens.map((s) => s.key));

/** URL の # のあと（#/transports など）から、画面を決める。何もないときや知らない名前は、マップの画面。 */
export function screenFromHash(hash: string): ScreenKey {
  const key = hash.replace(/^#\/?/, '');
  return KEYS.has(key) ? (key as ScreenKey) : 'map';
}

export function hashOf(screen: ScreenKey): string {
  return `#/${screen}`;
}

/** 今の画面（URL の # のあとで覚えるので、読み込み直しても同じ画面を開く）と、画面を変える関数。 */
export function useScreen(): readonly [ScreenKey, (screen: ScreenKey) => void] {
  const [screen, setScreen] = useState<ScreenKey>(() => screenFromHash(window.location.hash));
  useEffect(() => {
    const onHashChange = () => setScreen(screenFromHash(window.location.hash));
    window.addEventListener('hashchange', onHashChange);
    return () => window.removeEventListener('hashchange', onHashChange);
  }, []);
  const navigate = (next: ScreenKey) => {
    window.location.hash = hashOf(next);
    setScreen(next);
  };
  return [screen, navigate];
}
