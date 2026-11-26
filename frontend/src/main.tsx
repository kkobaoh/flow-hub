import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './app/App';
import { SetupPage } from './pages/SetupPage';
import { UserLoginPage } from './pages/UserLoginPage';
import { createSignalRLink } from './app/realtime/signalRLink';
import './styles/global.css';

const root = document.getElementById('root');
if (!root) {
  throw new Error('index.html に #root がありません。');
}

/** サーバーとのつながり。開発用のサーバー（Vite）では、プロキシを通して FlowHub.Api へつなぐ。 */
function currentPage() {
  switch (window.location.pathname) {
    case '/setup': return <SetupPage />;
    case '/login': return <UserLoginPage />;
    default: return <App link={createSignalRLink()} />;
  }
}

createRoot(root).render(
  <StrictMode>
    {currentPage()}
  </StrictMode>,
);
