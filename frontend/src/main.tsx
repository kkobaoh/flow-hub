import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './app/App';
import { createSignalRLink } from './app/realtime/signalRLink';
import './styles/global.css';

const root = document.getElementById('root');
if (!root) {
  throw new Error('index.html に #root がありません。');
}

/** サーバーとのつながり。開発用のサーバー（Vite）では、プロキシを通して FlowHub.Api へつなぐ。 */
const link = createSignalRLink();

createRoot(root).render(
  <StrictMode>
    <App link={link} />
  </StrictMode>,
);
