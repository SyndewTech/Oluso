import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Toaster } from 'react-hot-toast';
import './index.css';

// Import the Account UI library
import { AccountApp, setApiBaseUrl } from '@oluso/account-ui';

// Import account plugins
import { createFido2AccountPlugin } from '@oluso/fido2-ui';
import { apiClient } from '@oluso/account-ui';

// Runtime config (supports Cloudflare Pages env vars)
import { config } from './config';

// Configure API base URL
const apiBaseUrl = config.apiUrl;
setApiBaseUrl(apiBaseUrl);

// Create account plugins
const plugins = [
  createFido2AccountPlugin({ apiClient }),
];

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
        <AccountApp plugins={plugins} />
        <Toaster position="top-right" />
      </BrowserRouter>
    </QueryClientProvider>
  </React.StrictMode>
);
