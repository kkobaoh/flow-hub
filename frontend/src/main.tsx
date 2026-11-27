import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './app/App';
import { SetupPage } from './pages/SetupPage';
import { UserLoginPage } from './pages/UserLoginPage';
import { createSignalRLink } from './app/realtime/signalRLink';
import { watchScreenSession } from './shared/api/screenSession';
import './styles/global.css';

const root = document.getElementById('root');
if (!root) {
  throw new Error('index.html に #root がありません。');
}

/** サーバーとのつながり。開発用のサーバー（Vite）では、プロキシを通して FlowHub.Api へつなぐ。 */
const appRoot = createRoot(root);
function currentPage() {
  switch (window.location.pathname) {
    case '/setup': return <SetupPage />;
    case '/login': return <UserLoginPage changeOwn={new URLSearchParams(window.location.search).has('change-password')} />;
    default:
      watchScreenSession(() => {
        appRoot.unmount();
        window.location.replace('/login');
      });
      return <App link={createSignalRLink()} />;
  }
}

appRoot.render(
  <StrictMode>
    {currentPage()}
  </StrictMode>,
);
