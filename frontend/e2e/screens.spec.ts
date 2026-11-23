import { expect, test, type Page } from '@playwright/test';

/** 画面で起きたエラー（例外と console.error）を集める。どのテストも、最後に空であることを確かめる。 */
function collectErrors(page: Page): string[] {
  const errors: string[] = [];
  page.on('pageerror', (e) => errors.push(e.message));
  page.on('console', (m) => {
    if (m.type() === 'error') {
      errors.push(m.text());
    }
  });
  return errors;
}

async function openMenu(page: Page, screen: string) {
  await page.getByRole('button', { name: 'メニュー' }).click();
  await page.locator(`[data-screen="${screen}"]`).click();
}

const screens: readonly (readonly [string, string])[] = [
  ['transports', '搬送の一覧'],
  ['devices', '装置とストッカー'],
  ['station', 'ステーションの在庫'],
  ['consumables', '消耗品の検索と手入力'],
  ['mapEdit', 'マップの編集'],
  ['agvs', 'AGV と AGV の種類の登録'],
  ['consumableConfig', '消耗品の構成'],
  ['users', 'ユーザーとロールの管理'],
];

test('入り口はマップの画面で、メニューからどの画面へもエラーなく移れる', async ({ page }) => {
  const errors = collectErrors(page);
  await page.goto('/');
  await expect(page.getByRole('img', { name: 'マップ' })).toBeVisible();

  for (const [key, title] of screens) {
    await openMenu(page, key);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(title);
    await expect(page).toHaveURL(new RegExp(`#/${key}$`));
  }
  expect(errors).toEqual([]);
});

test('搬送の一覧：中断した搬送を、確かめる画面で取りやめる', async ({ page }) => {
  const errors = collectErrors(page);
  await page.goto('/#/transports');

  await expect(page.getByRole('tab', { name: /中断した/ })).toHaveAttribute('aria-selected', 'true');
  const row = page.getByRole('row', { name: /#11/ });
  await row.getByRole('button', { name: '取りやめる' }).click();
  const dialog = page.getByRole('alertdialog');
  await expect(dialog.getByRole('button', { name: 'やめる', exact: true })).toBeFocused();
  await dialog.getByRole('button', { name: '取りやめる' }).click();

  await expect(page.getByText('中断した搬送はありません。')).toBeVisible();
  expect(errors).toEqual([]);
});

test('URL で指定した装置を、読み込み直しても出す', async ({ page }) => {
  const errors = collectErrors(page);
  await page.goto('/#/devices/DEV-2');

  await expect(page.getByRole('combobox', { name: '装置' })).toHaveValue('DEV-2');
  await page.reload();
  await expect(page.getByRole('combobox', { name: '装置' })).toHaveValue('DEV-2');
  expect(errors).toEqual([]);
});

test('マップの編集：パネルを置いて保存しないまま移ろうとすると、確かめる画面を出す', async ({ page }) => {
  const errors = collectErrors(page);
  await page.goto('/#/mapEdit');

  await page.locator('[aria-label="置くもの"] button[data-tool="Normal"]').click();
  await page.locator('[data-cell]').first().click();
  await expect(page.getByText('保存していない変更があります')).toBeVisible();

  await openMenu(page, 'transports');
  await expect(page.getByRole('alertdialog')).toContainText('保存していない変更を捨てて移りますか？');
  await page.getByRole('button', { name: 'やめる', exact: true }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('マップの編集');

  await page.getByRole('button', { name: '下書きを保存する' }).click();
  await openMenu(page, 'transports');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('搬送の一覧');
  expect(errors).toEqual([]);
});
