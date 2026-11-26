import { expect, test } from '@playwright/test';

test('初期設定は一度だけ完了でき、もう一つのセッションからは管理者を増やせない', async ({ page, browser, baseURL }) => {
  const other = await browser.newContext({ baseURL });
  try {
    const second = await other.newPage();
    await second.goto('/#/users');
    await expect(second.getByRole('link', { name: 'ログイン画面へ' })).toBeVisible();
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
    await page.goto('/#/users');
    await expect(page.getByRole('cell', { name: 'first-admin', exact: true })).toBeVisible();
    await expect(page.getByRole('cell', { name: 'admin01', exact: true })).toHaveCount(0);
    await page.getByRole('tab', { name: 'ロール' }).click();
    await expect(page.getByRole('checkbox', { name: '管理者：ユーザーとロールの管理' })).toBeChecked();
    await page.getByRole('tab', { name: 'ユーザー', exact: true }).click();
    await page.getByRole('button', { name: 'ユーザーを加える' }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('ユーザー ID').fill('operator02');
    await dialog.getByRole('button', { name: 'ユーザーを加える' }).click();
    await expect(dialog).toContainText('operator02 の仮のパスワード');
    const operatorPassword = await dialog.locator('p.select-all').innerText();
    await dialog.getByRole('button', { name: '閉じる' }).click();
    const loginForm = await second.request.get('/identity/user/form');
    const loginHeaders = { 'X-FlowHub-CSRF': (await loginForm.json()).requestToken };
    for (let attempt = 1; attempt <= 5; attempt++) {
      const denied = await second.request.post('/identity/user/login', {
        headers: loginHeaders, data: { userId: 'operator02', password: 'wrong-password' },
      });
      expect(denied.status()).toBe(attempt === 5 ? 429 : 401);
    }
    await page.reload();
    const operatorRow = page.getByRole('row').filter({ has: page.getByRole('cell', { name: 'operator02', exact: true }) });
    await expect(operatorRow).toContainText('ロック中');
    await operatorRow.getByRole('button', { name: 'ロックを外す' }).click();
    await expect(operatorRow.getByRole('button', { name: 'ロックを外す' })).toHaveCount(0);
    await second.getByLabel('ユーザー ID').fill('operator02');
    await second.getByLabel('パスワード', { exact: true }).fill(operatorPassword);
    await second.getByRole('button', { name: 'ログイン' }).click();
    await second.getByLabel('新しいパスワード').fill('operator-password');
    await second.getByRole('button', { name: '変更する' }).click();
    await expect(second.getByRole('button', { name: 'ログアウト' })).toBeVisible();
    await expect(page.getByRole('cell', { name: 'operator02', exact: true })).toBeVisible();
    await page.reload();
    await expect(page.getByLabel('operator02 のロール')).toHaveValue('viewer');
    const firstAdminRole = page.getByLabel('first-admin のロール');
    const operatorRole = page.getByLabel('operator02 のロール');
    await expect(firstAdminRole).toBeDisabled();
    await operatorRole.selectOption('admin');
    await expect(firstAdminRole).toBeEnabled();
    await page.reload();
    await expect(operatorRole).toHaveValue('admin');
    await firstAdminRole.selectOption('viewer');
    await expect(page.getByRole('button', { name: 'ユーザーを加える' })).toBeDisabled();
    await expect(operatorRole).toBeDisabled();
    await second.goto('/#/users');
    const restoredAdmin = second.getByLabel('first-admin のロール');
    await expect(restoredAdmin).toBeEnabled();
    await restoredAdmin.selectOption('admin');
    await expect(second.getByLabel('operator02 のロール')).toBeEnabled();

    // 別々の管理者が同時に自分を降格させても、DB には必ず管理者が残る。
    const attempts = await Promise.all(([ [page, 'first-admin'], [second, 'operator02'] ] as const)
      .map(async ([tab, userId]) => {
        const form = await tab.request.get('/identity/user/session');
        const { requestToken } = await form.json();
        const response = await tab.request.post('/identity/user/users/role', {
          data: { userId, roleId: 'viewer' }, headers: { 'X-FlowHub-CSRF': requestToken },
        });
        return response.status();
      }));
    expect(attempts.sort()).toEqual([204, 409]);
    const directory = await second.request.get('/identity/user/users');
    const accounts: { role: string }[] = await directory.json();
    expect(accounts.filter(user => user.role === 'admin')).toHaveLength(1);
  } finally {
    await other.close();
  }
});
