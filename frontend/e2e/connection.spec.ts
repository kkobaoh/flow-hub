import { expect, test, type Page } from '@playwright/test';
import { collectErrors } from './errors';

const headerStatus = (page: Page) => page.locator('header [role="status"]');
const runSwitch = (page: Page) => page.getByRole('group', { name: '運転状態' });

/** 運転の切り替えを押し、確かめる画面で決める。 */
async function switchRun(page: Page, side: 'スタート' | '停止', confirm: 'スタートする' | '停止する'): Promise<void> {
  await runSwitch(page).getByRole('button', { name: side, exact: true }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: confirm }).click();
}

// サーバーの運転状態を変えるので、このファイルのテストは1つにまとめて、ほかと重ならないようにする
test('サーバーとつながり、運転の切り替えが、開いているすべての画面に届く', async ({ page, context }) => {
  const errors = collectErrors(page);
  await page.goto('/');

  // 開発用の設定では、Api は起動したらすぐスタートにする
  await expect(headerStatus(page)).toHaveText('運転中');
  await expect(page.getByText('サーバーとつながっていません')).toHaveCount(0);
  await expect(page.getByText('サーバーとつないでいます')).toHaveCount(0);

  const other = await context.newPage();
  const otherErrors = collectErrors(other);
  await other.goto('/');
  await expect(headerStatus(other)).toHaveText('運転中');

  await switchRun(page, '停止', '停止する');
  await expect(headerStatus(page)).toHaveText('停止中');
  await expect(headerStatus(other)).toHaveText('停止中'); // 別の画面にも届く

  await switchRun(other, 'スタート', 'スタートする');
  await expect(headerStatus(other)).toHaveText('運転中');
  await expect(headerStatus(page)).toHaveText('運転中');

  expect(errors).toEqual([]);
  expect(otherErrors).toEqual([]);
});

test('サーバーに登録されている AGV を描く。初期データの AGV は切り離しで位置がわからないので、マップの下の列に描く', async ({ page }) => {
  const errors = collectErrors(page);
  await page.goto('/');

  for (const id of ['AGV-1', 'AGV-2']) {
    const agv = page.locator(`[data-agv="${id}"]`);
    await expect(agv).toBeVisible();
    await expect(agv).toHaveAttribute('data-unplaced', '');
  }
  await expect(page.locator('[data-unplaced-title]')).toHaveText('位置がわからない AGV');

  await page.locator('[data-agv="AGV-2"]').click();
  await expect(page.locator('[data-agv-card]')).toContainText('位置がわからない');
  expect(errors).toEqual([]);
});
