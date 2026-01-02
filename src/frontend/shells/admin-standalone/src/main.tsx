import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Toaster } from 'react-hot-toast';
import './index.css';

// Import the Admin UI library
import { AdminApp, setApiBaseUrl, apiClient } from '@oluso/admin-ui';

// Import enterprise plugins
import { createSamlPlugin } from '@oluso/saml-ui';
import { createFido2Plugin } from '@oluso/fido2-ui';
import { createScimPlugin } from '@oluso/scim-ui';
import { createTelemetryPlugin } from '@oluso/telemetry-ui';

// Configure API base URL from environment
const serverBaseUrl = import.meta.env.VITE_SERVER_URL || 'http://localhost:5050';
const apiBaseUrl = import.meta.env.VITE_API_URL || `${serverBaseUrl}/api/admin`;
setApiBaseUrl(apiBaseUrl);

// Create enterprise plugins
const plugins = [
  createSamlPlugin({ apiClient }),
  createFido2Plugin({ apiClient }),
  createScimPlugin({ apiClient, serverBaseUrl }),
  createTelemetryPlugin({ apiClient }),
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
        <AdminApp plugins={plugins} />
        <Toaster position="top-right" />
      </BrowserRouter>
    </QueryClientProvider>
  </React.StrictMode>
);
