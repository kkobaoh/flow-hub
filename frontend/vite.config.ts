import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig(({ mode }) => {
  /** 開発用のサーバーから、つなぎに行く FlowHub.Api の場所。環境変数 FLOWHUB_API_URL で変えられる（E2E では、テストが起こした Api を指す）。 */
  const api = loadEnv(mode, '.', 'FLOWHUB_').FLOWHUB_API_URL ?? 'http://localhost:5020';
  return {
    plugins: [react(), tailwindcss()],
    server: {
      // 画面とサーバーの通信（SignalR のハブと HTTP の窓口）は、開発用のサーバーを通して Api へ渡す
      proxy: {
        '/hubs': { target: api, ws: true },
        '/system': { target: api },
        '/transports': { target: api },
        '/agvs': { target: api },
        '/agv-types': { target: api },
        '/agv-registrations': { target: api },
        '/configuration': { target: api },
        '/devices': { target: api },
        '/consumables': { target: api },
      },
    },
  };
});
