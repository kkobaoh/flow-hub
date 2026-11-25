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

test('消耗品の検索：サーバーの消耗品の記録から探す', async ({ page }) => {
  const errors = collectErrors(page);
  await page.goto('/#/consumables');

  // 初期データには消耗品がないので、見つからないことを確かめる
  await page.getByRole('searchbox').fill('NO-SUCH-ID');
  await page.getByRole('button', { name: '探す' }).click();
  await expect(page.getByText('「NO-SUCH-ID」に合う消耗品はありません。ID を確かめてください。')).toBeVisible();
  await expect(page.getByText('この画面は、見本のデータです')).toHaveCount(0);
  expect(errors).toEqual([]);
});

test('消耗品の検索：在庫を手入力で登録して開き、消す', async ({ page }) => {
  const errors = collectErrors(page);
  await page.goto('/#/consumables');

  // 中古品にしておく（届けるものにならないので、ほかのテストの搬送の様子を変えない）
  await page.getByRole('button', { name: '手入力で登録する' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('textbox', { name: 'ID' }).fill('E2E-1');
  await dialog.getByRole('combobox', { name: '品種' }).selectOption('DR-5');
  await dialog.getByRole('radio', { name: '中古品' }).check();
  await dialog.getByRole('combobox', { name: '所在' }).selectOption({ label: 'ST-1' });
  await dialog.getByRole('button', { name: '記録を保存する' }).click();

  await page.getByRole('searchbox').fill('E2E-1');
  await page.getByRole('button', { name: '探す' }).click();
  await expect(page.locator('[aria-labelledby="item-title"]')).toContainText('DR-5・中古品');

  await page.getByRole('button', { name: '記録を消す' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: '記録を消す' }).click();
  await expect(page).toHaveURL(/#\/consumables$/);
  await page.getByRole('searchbox').fill('E2E-1');
  await page.getByRole('button', { name: '探す' }).click();
  await expect(page.getByText('「E2E-1」に合う消耗品はありません。ID を確かめてください。')).toBeVisible();
  expect(errors).toEqual([]);
});

test('AGV と AGV の種類の登録：サーバーの種類を出し、種類を加えて消す', async ({ page }) => {
  const errors = collectErrors(page);
  await page.goto('/#/agvs');

  const types = page.locator('[aria-labelledby="type-title"]');
  await expect(types.getByRole('row', { name: /標準の AGV/ })).toBeVisible(); // 初期データの種類

  await types.getByRole('button', { name: '種類を加える' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('textbox', { name: '種類の ID' }).fill('e2e-type');
  await dialog.getByRole('textbox', { name: '種類の名前' }).fill('E2E の種類');
  await dialog.getByRole('spinbutton', { name: '前進の速さ' }).fill('600');
  await dialog.getByRole('spinbutton', { name: '後退の速さ' }).fill('300');
  await dialog.getByRole('spinbutton', { name: '回転の速さ' }).fill('45');
  await dialog.getByRole('button', { name: '種類を保存する' }).click();

  const row = types.getByRole('row', { name: /E2E の種類/ });
  await expect(row).toContainText('e2e-type');
  await row.getByRole('button', { name: '消す' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: '種類を消す' }).click();
  await expect(types.getByRole('row', { name: /E2E の種類/ })).toHaveCount(0);
  expect(errors).toEqual([]);
});

test('AGV と AGV の種類の登録：AGV を加えて切り離しから始め、消す', async ({ page }) => {
  const errors = collectErrors(page);
  await page.goto('/#/agvs');

  const agvs = page.locator('[aria-labelledby="agv-title"]');
  await agvs.getByRole('button', { name: 'AGV を加える' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('textbox', { name: 'AGV の番号' }).fill('AGV-E2E');
  await dialog.getByRole('combobox', { name: 'AGV の種類' }).selectOption({ label: '標準の AGV' });
  await dialog.getByRole('combobox', { name: 'キャリアの種類' }).selectOption({ index: 1 });
  await dialog.getByRole('button', { name: 'AGV を保存する' }).click();

  const row = agvs.getByRole('row', { name: /AGV-E2E/ });
  await expect(row).toContainText('切り離し');
  await expect(row).toContainText('標準の AGV');
  await row.getByRole('button', { name: '消す' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'AGV を消す' }).click();
  await expect(agvs.getByRole('row', { name: /AGV-E2E/ })).toHaveCount(0);
  expect(errors).toEqual([]);
});

test('消耗品の構成：加工の種類と品種を加え、入庫の予定を加えて外し、消す', async ({ page }) => {
  const errors = collectErrors(page);
  await page.goto('/#/consumableConfig');

  await expect(page.getByRole('row', { name: /drill/ })).toBeVisible(); // 初期データの加工の種類
  await page.getByRole('button', { name: '加工の種類を加える' }).click();
  let dialog = page.getByRole('dialog');
  await dialog.getByRole('textbox', { name: '加工の種類の名前' }).fill('e2e-kind');
  await dialog.getByLabel('キャリアの種類').fill('e2e-carrier');
  await dialog.getByRole('spinbutton', { name: 'キャリアに入る個数' }).fill('3');
  await dialog.getByRole('button', { name: '加工の種類を保存する' }).click();
  await expect(page.getByRole('row', { name: /e2e-kind/ })).toContainText('e2e-carrier');

  await page.getByRole('tab', { name: '品種' }).click();
  await page.getByRole('button', { name: '品種を加える' }).click();
  dialog = page.getByRole('dialog');
  await dialog.getByRole('textbox', { name: '品種の名前' }).fill('E2E-1');
  await dialog.getByRole('combobox', { name: '加工の種類' }).selectOption('e2e-kind');
  await dialog.getByRole('button', { name: '品種を保存する' }).click();
  const variety = page.getByRole('row', { name: /E2E-1/ });
  await expect(variety).toContainText('e2e-kind');

  await variety.getByRole('button', { name: 'ID の対応' }).click();
  dialog = page.getByRole('dialog');
  await dialog.getByRole('textbox', { name: '加える ID' }).fill('E2E-ID-1');
  await dialog.getByRole('button', { name: '加える', exact: true }).click();
  await expect(dialog.getByRole('listitem')).toContainText('E2E-ID-1');
  await dialog.getByRole('button', { name: '外す' }).click();
  await expect(dialog.getByRole('listitem')).toHaveCount(0);
  await dialog.getByRole('button', { name: '閉じる' }).click();

  await variety.getByRole('button', { name: '消す' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: '品種を消す' }).click();
  await expect(page.getByRole('row', { name: /E2E-1/ })).toHaveCount(0);
  await page.getByRole('tab', { name: '加工の種類' }).click();
  await page.getByRole('row', { name: /e2e-kind/ }).getByRole('button', { name: '消す' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: '加工の種類を消す' }).click();
  await expect(page.getByRole('row', { name: /e2e-kind/ })).toHaveCount(0);
  expect(errors).toEqual([]);
});
