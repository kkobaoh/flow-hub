import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { App } from './App';

describe('App', () => {
  it('システムの名前を見出しに出す', () => {
    const html = renderToStaticMarkup(<App />);

    expect(html).toContain('<h1');
    expect(html).toContain('FlowHub');
  });

  it('床の色の上に、グラファイトの字で描く', () => {
    const html = renderToStaticMarkup(<App />);

    expect(html).toContain('bg-ground');
    expect(html).toContain('text-graphite');
  });
});
