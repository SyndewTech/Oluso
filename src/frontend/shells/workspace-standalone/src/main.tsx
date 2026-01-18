import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Toaster } from 'react-hot-toast';

// Import styles
import './index.css';

// Import the Workspace Portal library
import { WorkspaceApp, setApiBaseUrl, createOidcConfig } from '@oluso/workspace-ui';
import type { WorkspaceUIPlugin } from '@oluso/workspace-ui';

// Runtime config
import { config } from './config';

// Set runtime config for the library
if (typeof window !== 'undefined') {
  window.__OLUSO_CONFIG__ = {
    serverUrl: config.serverUrl,
    apiUrl: config.apiUrl,
    clientId: config.clientId,
    scopes: config.scopes,
  };
}

// Configure API base URL
setApiBaseUrl(config.apiUrl);

// Create OIDC config
const oidcConfig = createOidcConfig({
  authority: config.serverUrl,
  client_id: config.clientId,
  scope: config.scopes,
  redirect_uri: `${window.location.origin}/callback`,
  post_logout_redirect_uri: window.location.origin,
});

// Plugins can be added here
const plugins: WorkspaceUIPlugin[] = [];

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 5 * 60 * 1000,
      retry: 1,
    },
  },
});

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <WorkspaceApp plugins={plugins} oidcConfigOverrides={oidcConfig} />
        <Toaster position="top-right" />
      </BrowserRouter>
    </QueryClientProvider>
  </React.StrictMode>
);
