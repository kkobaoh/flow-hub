import { afterEach, describe, expect, it } from 'vitest';
import { clickedInside } from './outside';

afterEach(() => {
  document.body.innerHTML = '';
});

describe('clickedInside（押したところが、その範囲の中か）', () => {
  it('範囲の中と外を見分ける', () => {
    document.body.innerHTML = '<div data-keep=""><button id="in"></button></div><button id="out"></button>';
    const seen: boolean[] = [];
    document.addEventListener('click', (e) => seen.push(clickedInside(e, '[data-keep]')), { once: true });
    document.getElementById('in')!.click();
    document.addEventListener('click', (e) => seen.push(clickedInside(e, '[data-keep]')), { once: true });
    document.getElementById('out')!.click();

    expect(seen).toEqual([true, false]);
  });

  it('押したものが、届くまでに画面から外されても、押したときの中と見る（一覧の行を押すとカードに替わるときなど）', () => {
    document.body.innerHTML = '<div data-keep=""><ul id="list"><li><button id="row"></button></li></ul></div>';
    const row = document.getElementById('row')!;
    row.addEventListener('click', () => document.getElementById('list')!.remove());
    let inside: boolean | null = null;
    document.addEventListener('click', (e) => (inside = clickedInside(e, '[data-keep]')), { once: true });

    row.click();

    expect(inside).toBe(true);
  });
});
