import { expect, test } from '@playwright/test';
import { collectErrors, openMenu } from './errors';

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

test('搬送の一覧：サーバーの搬送の一覧を、段階ごとのタブで出す', async ({ page }) => {
  const errors = collectErrors(page);
  await page.goto('/#/transports');

  // 搬送があるかどうかは、そのときのシミュレーターの動きによるので、タブがそろうことだけを確かめる
  const tabs = page.getByRole('tablist', { name: '搬送の段階' });
  await expect(tabs.getByRole('tab')).toHaveText([/^待ち行列\d+$/, /^走っている\d+$/, /^中断した\d+$/]);
  await expect(page.getByText('この画面は、見本のデータです')).toHaveCount(0);
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

test('装置とストッカー：サーバーの装置の中を出し、ストッカーの設定をサーバーへ保存する', async ({ page }) => {
  const errors = collectErrors(page);
  await page.goto('/#/devices/DEV-2');

  const inside = page.locator('[aria-labelledby="inside-title"]');
  await expect(inside).toContainText('スロット10'); // 初期データの DEV-2 は、スロットが 10
  const setting = page.locator('[aria-labelledby="setting-title"]');
  await expect(setting.getByRole('row', { name: /DR-5/ })).toBeVisible();

  // 同じ個数のまま保存する（ほかのテストの搬送の様子を変えないため）
  await setting.getByRole('button', { name: '設定を変える（段取り替え）' }).click();
  await setting.getByRole('spinbutton', { name: 'DR-5 の設定の個数' }).fill('2');
  await setting.getByRole('button', { name: '設定を保存する' }).click();

  await expect(setting.getByRole('spinbutton')).toHaveCount(0);
  await expect(setting.getByRole('alert')).toHaveCount(0);
  expect(errors).toEqual([]);
});

test('ステーションの在庫：サーバーの品種ごとの数を出す', async ({ page }) => {
  const errors = collectErrors(page);
  await page.goto('/#/station');

  const summary = page.locator('[aria-labelledby="summary-title"]');
  await expect(summary.getByRole('row', { name: /DR-5/ })).toBeVisible();
  await expect(summary.getByRole('row', { name: /DR-8/ })).toBeVisible();
  expect(errors).toEqual([]);
});
