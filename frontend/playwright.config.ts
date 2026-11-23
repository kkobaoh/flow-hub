import { defineConfig, devices } from '@playwright/test';

const port = 5174;

/**
 * 画面を本物のブラウザーで開いて確かめる E2E テスト（npm run e2e）。
 * ふだんの npm test（Vitest）には含めない。開発用のサーバーは、テストが自分で起こす。
 */
export default defineConfig({
  testDir: 'e2e',
  fullyParallel: true,
  reporter: 'list',
  use: { baseURL: `http://localhost:${port}`, trace: 'off' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: `npm run dev -- --port ${port} --strictPort`,
    url: `http://localhost:${port}`,
    reuseExistingServer: true,
  },
});
