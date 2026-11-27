import { defineConfig, devices } from '@playwright/test';

// 開発で使う 5173 や、ほかの確かめで起こすサーバー（5174、5175 など）と重ならないポートにする
const port = 5190;
const apiPort = 5199;
/** E2E で起こす Api（テストから AGV の役をするときにも使う）。 */
export const api = `http://localhost:${apiPort}`;
export const screenSessionFile = 'test-results/.auth/screen.json';

/** E2E 用の DB（SQLite）のフォルダー（Api のフォルダーから見た場所）。開発用の DB とは分ける。空なら、開発用の初期データが入る。 */
const database = '../../frontend/.e2e-data';

/**
 * 画面を本物のブラウザーで開いて確かめる E2E テスト（npm run e2e）。
 * ふだんの npm test（Vitest）には含めない。サーバー（FlowHub.Api）と開発用のサーバーは、テストが自分で起こす。
 * PC の幅（chromium）と、スマートフォンの幅（phone。phone.spec.ts だけ）で流す。
 */
export default defineConfig({
  testDir: 'e2e',
  fullyParallel: true,
  reporter: 'list',
  use: { baseURL: `http://localhost:${port}`, trace: 'off' },
  projects: [
    { name: 'setup', use: { ...devices['Desktop Chrome'] }, testMatch: /setup\.spec\.ts/ },
    { name: 'chromium', dependencies: ['setup'], use: { ...devices['Desktop Chrome'], storageState: screenSessionFile }, testIgnore: /(?:phone|setup)\.spec\.ts/ },
    { name: 'phone', dependencies: ['setup'], use: { ...devices['Pixel 7'], storageState: screenSessionFile }, testMatch: /phone\.spec\.ts/ },
  ],
  webServer: [
    {
      // 前の回の状態（コントロールステータスなど）が残らないように、E2E 用の DB を消してから起こす
      command: `node -e "require('fs').rmSync('${database}', { recursive: true, force: true })" && dotnet run --no-launch-profile`,
      cwd: '../src/FlowHub.Api',
      url: `${api}/system/status`,
      timeout: 180_000,
      // いつも、この設定で起こし直す（起こしたままの別のサーバーを使うと、つなぐ先や DB が違ってしまう）
      reuseExistingServer: false,
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        ASPNETCORE_URLS: api,
        Database__SqliteDirectory: database,
        Identity__SetupPassword: 'e2e-only-setup-password',
      },
    },
    {
      command: `npm run dev -- --port ${port} --strictPort`,
      url: `http://localhost:${port}`,
      reuseExistingServer: false,
      env: { FLOWHUB_API_URL: api },
    },
  ],
});
