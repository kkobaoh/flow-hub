import type { Page } from '@playwright/test';

/** 画面で起きたエラー（例外と console.error）を集める。テストの最後に、空であることを確かめる。 */
export function collectErrors(page: Page): string[] {
  const errors: string[] = [];
  page.on('pageerror', (e) => errors.push(e.message));
  page.on('console', (m) => {
    if (m.type() === 'error') {
      errors.push(m.text());
    }
  });
  return errors;
}

/** メニューを開いて、画面を選ぶ。 */
export async function openMenu(page: Page, screen: string): Promise<void> {
  await page.getByRole('button', { name: 'メニュー' }).click();
  await page.locator(`[data-screen="${screen}"]`).click();
}
