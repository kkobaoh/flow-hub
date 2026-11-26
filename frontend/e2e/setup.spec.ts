import { expect, test } from '@playwright/test';

test('初期設定は一度だけ完了でき、もう一つのセッションからは管理者を増やせない', async ({ page, browser, baseURL }) => {
  const other = await browser.newContext({ baseURL });
  try {
    const second = await other.newPage();
    for (const tab of [page, second]) {
      await tab.goto('/setup');
      await tab.getByLabel('設置先のパスワード').fill('e2e-only-setup-password');
      await tab.getByRole('button', { name: 'ログイン' }).click();
      await expect(tab.getByLabel('管理者 ID')).toBeVisible();
    }
    await page.getByLabel('管理者 ID').fill('first-admin');
    await page.getByRole('button', { name: '管理者を作成する' }).click();
    await expect(page.locator('output')).toBeVisible();
    const temporary = await page.locator('output').innerText();
    await page.getByRole('button', { name: '閉じる' }).click();
    await expect(page.locator('output')).toHaveCount(0);
    await second.getByLabel('管理者 ID').fill('another-admin');
    await second.getByRole('button', { name: '管理者を作成する' }).click();
    await expect(second.getByRole('alert')).toHaveText('最初の管理者はすでに作成されています。');
    for (const tab of [page, second]) {
      await tab.goto('/login');
      await tab.getByLabel('ユーザー ID').fill('first-admin');
      await tab.getByLabel('パスワード', { exact: true }).fill(temporary);
      await tab.getByRole('button', { name: 'ログイン' }).click();
      await expect(tab.getByLabel('新しいパスワード')).toBeVisible();
    }
    await page.getByLabel('新しいパスワード').fill(' 新admin123👨‍👩‍👦');
    await page.getByRole('button', { name: '変更する' }).click();
    await expect(page.getByRole('button', { name: 'ログアウト' })).toBeVisible();
    await second.reload();
    await expect(second.getByLabel('ユーザー ID')).toBeVisible();
    await page.getByRole('button', { name: 'ログアウト' }).click();
    await page.getByLabel('ユーザー ID').fill('first-admin');
    await page.getByLabel('パスワード', { exact: true }).fill(temporary);
    await page.getByRole('button', { name: 'ログイン' }).click();
    await expect(page.getByRole('alert')).toContainText('一致しません');
    await page.getByLabel('パスワード', { exact: true }).fill(' 新admin123👨‍👩‍👦');
    await page.getByRole('button', { name: 'ログイン' }).click();
    await expect(page.getByRole('button', { name: 'ログアウト' })).toBeVisible();
  } finally {
    await other.close();
  }
});
