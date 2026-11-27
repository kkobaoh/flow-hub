import { expect, test, type APIRequestContext } from '@playwright/test';

async function submit(request: APIRequestContext, path: string, data: Record<string, string>, form = 'session') {
  const token = await request.get('/identity/user/' + form);
  expect(token.ok()).toBe(true);
  const response = await request.post('/identity/user/' + path, {
    data, headers: { 'X-FlowHub-CSRF': (await token.json()).requestToken },
  });
  expect(response.ok()).toBe(true);
  return response;
}

test('仮パスワード再発行後は接続済みの画面も次のポーリングでログイン画面に移る', async ({ request, browser, baseURL }) => {
  const userId = 'screen-session-test';
  const added = await submit(request, 'users', { userId });
  const viewer = await browser.newContext({ baseURL });
  try {
    await submit(viewer.request, 'login', { userId, password: (await added.json()).temporaryPassword }, 'form');
    await submit(viewer.request, 'initial-password', { password: 'screen-session-password' });
    const page = await viewer.newPage();
    await page.goto('/');
    await expect(page.locator('[data-agv="AGV-1"]')).toBeVisible();
    const refused = page.waitForResponse(r => r.url().includes('/hubs/screen?') && r.request().method() === 'GET' && r.status() === 401);
    await submit(request, 'users/temporary-password', { userId });
    await refused;
    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByLabel('ユーザー ID')).toBeVisible();
  } finally {
    await viewer.close();
  }
});
