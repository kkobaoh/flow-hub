import { useEffect, useState } from 'react';
import { screens, type ScreenKey } from './screens';

const KEYS: ReadonlySet<string> = new Set(screens.map((s) => s.key));

/** 開いている画面と、その画面で開くもの（装置の ID など。ないときは null）。 */
export interface Route {
  readonly screen: ScreenKey;
  readonly target: string | null;
}

const MAP: Route = { screen: 'map', target: null };

/** URL の # のあと（#/devices/DEV-2 など）から、画面を決める。何もないときや知らない名前は、マップの画面。 */
export function routeFromHash(hash: string): Route {
  const [key = '', ...rest] = hash.replace(/^#\/?/, '').split('/');
  if (!KEYS.has(key)) {
    return MAP;
  }
  const target = rest.join('/');
  return { screen: key as ScreenKey, target: target === '' ? null : decodeURIComponent(target) };
}

export function hashOf(route: Route): string {
  return route.target === null ? `#/${route.screen}` : `#/${route.screen}/${encodeURIComponent(route.target)}`;
}

/** 今の画面（URL の # のあとで覚えるので、読み込み直しても同じ画面を開く）と、画面を変える関数。 */
export function useRoute(): readonly [Route, (screen: ScreenKey, target?: string) => void] {
  const [route, setRoute] = useState<Route>(() => routeFromHash(window.location.hash));
  useEffect(() => {
    const onHashChange = () => setRoute(routeFromHash(window.location.hash));
    window.addEventListener('hashchange', onHashChange);
    return () => window.removeEventListener('hashchange', onHashChange);
  }, []);
  const navigate = (screen: ScreenKey, target?: string) => {
    const next = { screen, target: target ?? null };
    window.location.hash = hashOf(next);
    setRoute(next);
  };
  return [route, navigate];
}
