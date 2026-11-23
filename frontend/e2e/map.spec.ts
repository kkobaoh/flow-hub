import { expect, test, type Page } from '@playwright/test';
import { collectErrors } from './errors';

const mapSvg = (page: Page) => page.locator('[data-map-surface] svg[aria-label="マップ"]');
const card = (page: Page) => page.locator('[data-agv-card]');

async function viewBox(page: Page): Promise<{ x: number; y: number; width: number; height: number }> {
  const text = await mapSvg(page).getAttribute('viewBox');
  const [x = 0, y = 0, width = 0, height = 0] = (text ?? '').split(' ').map(Number);
  return { x, y, width, height };
}

async function surfaceCenter(page: Page): Promise<{ x: number; y: number }> {
  const box = await page.locator('[data-map-surface]').boundingBox();
  if (box === null) {
    throw new Error('マップが描かれていません。');
  }
  return { x: box.x + box.width / 2, y: box.y + box.height / 2 };
}

test.beforeEach(async ({ page }) => {
  await page.goto('/');
  await expect(mapSvg(page)).toBeVisible();
});

test('ドラッグでマップを動かす', async ({ page }) => {
  const errors = collectErrors(page);
  const before = await viewBox(page);
  const c = await surfaceCenter(page);

  await page.mouse.move(c.x, c.y);
  await page.mouse.down();
  await page.mouse.move(c.x + 150, c.y + 60, { steps: 8 });
  await page.mouse.up();

  const after = await viewBox(page);
  expect(after.x).toBeLessThan(before.x);
  expect(after.y).toBeLessThan(before.y);
  expect(after.width).toBeCloseTo(before.width);
  await expect(card(page)).toHaveCount(0);
  expect(errors).toEqual([]);
});

test('ホイールとボタンで拡大・縮小し、「表示の位置を戻す」で最初の大きさに戻す', async ({ page }) => {
  const errors = collectErrors(page);
  const first = await viewBox(page);
  const c = await surfaceCenter(page);

  await page.mouse.move(c.x, c.y);
  await page.mouse.wheel(0, -400);
  await expect.poll(async () => (await viewBox(page)).width).toBeLessThan(first.width);

  const zoomed = await viewBox(page);
  await page.getByRole('button', { name: '縮小する' }).click();
  await expect.poll(async () => (await viewBox(page)).width).toBeGreaterThan(zoomed.width);

  await page.getByRole('button', { name: '拡大する' }).click();
  await page.getByRole('button', { name: '拡大する' }).click();
  await page.getByRole('button', { name: '表示の位置を戻す' }).click();
  await expect.poll(() => viewBox(page)).toEqual(first);
  expect(errors).toEqual([]);
});

test('AGV を押すとカードが左から出て、外を押すと閉じる', async ({ page }) => {
  const errors = collectErrors(page);

  await page.locator('[data-agv="AGV-1"]').click();
  await expect(card(page)).toContainText('AGV-1');
  await expect(page.locator('[data-agv="AGV-1"]')).toHaveAttribute('data-emphasis', 'selected');
  await expect.poll(async () => (await card(page).boundingBox())?.x).toBe(0);

  // カードに重ならない、上の帯の右のほうを押す
  await page.locator('header').click({ position: { x: 700, y: 10 } });
  await expect(card(page)).toHaveCount(0);
  expect(errors).toEqual([]);
});

test('一覧の行にマウスを乗せるとマップで目立たせ、押すとカードを開く', async ({ page }) => {
  const errors = collectErrors(page);

  await page.getByRole('button', { name: '一覧', exact: true }).click();
  await page.locator('[data-row="AGV-2"]').hover();
  await expect(page.locator('[data-agv="AGV-2"]')).toHaveAttribute('data-emphasis', 'highlighted');

  await page.locator('[data-row="AGV-2"]').click();
  await expect(card(page)).toContainText('AGV-2');
  await expect(card(page).getByRole('button', { name: /一覧に戻る/ })).toBeVisible();
  expect(errors).toEqual([]);
});

test('装置を押してカードを開き、装置とストッカーの画面へ移る', async ({ page }) => {
  const errors = collectErrors(page);

  await page.locator('[data-equipment="DEV-1"]').click();
  await expect(card(page)).toContainText('DEV-1');
  await card(page).getByRole('button', { name: '装置とストッカーを開く' }).click();

  await expect(page).toHaveURL(/#\/devices\/DEV-1$/);
  await expect(page.getByRole('combobox', { name: '装置' })).toHaveValue('DEV-1');
  expect(errors).toEqual([]);
});
