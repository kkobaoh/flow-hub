import { expect, test, type Page } from '@playwright/test';
import { collectErrors, openMenu } from './errors';

/** スマートフォンの幅（playwright.config.ts の phone）で流す。 */

const screens = ['map', 'transports', 'devices', 'station', 'consumables', 'mapEdit', 'agvs', 'consumableConfig', 'users'] as const;

function viewport(page: Page): { width: number; height: number } {
  return page.viewportSize()!;
}

async function expectInside(page: Page, selector: string) {
  const box = await page.locator(selector).boundingBox();
  const v = viewport(page);
  expect(box).not.toBeNull();
  expect(box!.x).toBeGreaterThanOrEqual(0);
  expect(box!.x + box!.width).toBeLessThanOrEqual(v.width + 0.5);
}

/** 画面の下から出す欄：幅いっぱいで、下の端が画面の下に着き、高さは画面の6割まで（出てくる動きが終わるのを待つ）。 */
async function expectBottomSheet(page: Page, selector: string) {
  const v = viewport(page);
  await expect
    .poll(async () => {
      const b = await page.locator(selector).boundingBox();
      return b === null ? null : { x: Math.round(b.x), width: Math.round(b.width), bottom: Math.round(b.y + b.height), tall: b.height < v.height * 0.6 };
    })
    .toEqual({ x: 0, width: v.width, bottom: v.height, tall: true });
}

for (const screen of screens) {
  test(`${screen}：画面が横にはみ出さない`, async ({ page }) => {
    const errors = collectErrors(page);
    await page.goto(`/#/${screen}`);
    await expect(page.locator('header')).toBeVisible();

    // はみ出すと、スマートフォンのブラウザーは画面の幅（innerWidth）ごと広げるので、端末の幅と比べる
    const width = await page.evaluate(() => document.documentElement.scrollWidth);
    expect(width).toBeLessThanOrEqual(page.viewportSize()!.width);
    expect(errors).toEqual([]);
  });
}

test('メニューは画面の中に収まり、画面を選べる', async ({ page }) => {
  const errors = collectErrors(page);
  await page.goto('/#/transports');

  await page.getByRole('button', { name: 'メニュー' }).tap();
  await expectInside(page, '#main-menu');
  await page.locator('[data-screen="users"]').tap();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('ユーザーとロールの管理');
  expect(errors).toEqual([]);
});

test('マップ以外の画面：メッセージの列は、画面の下から出す', async ({ page }) => {
  const errors = collectErrors(page);
  await page.goto('/#/transports');

  await page.locator('button[aria-controls="message-rail"]').tap();
  await expectBottomSheet(page, '#message-rail');
  expect(errors).toEqual([]);
});

test('マップ：AGV を押すと、カードを画面の下から出す', async ({ page }) => {
  const errors = collectErrors(page);
  await page.goto('/');

  await page.locator('[data-agv="AGV-1"]').tap();
  await expect(page.locator('[data-agv-card]')).toContainText('AGV-1');
  await expectBottomSheet(page, '[data-agv-card]');
  expect(errors).toEqual([]);
});

test('マップ：メニューから、ほかの画面へ移れる', async ({ page }) => {
  const errors = collectErrors(page);
  await page.goto('/');

  await openMenu(page, 'station');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('ステーションの在庫');
  expect(errors).toEqual([]);
});
