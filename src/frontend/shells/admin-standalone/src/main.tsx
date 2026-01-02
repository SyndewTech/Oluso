import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Toaster } from 'react-hot-toast';

// Import AdminUI base styles first, then shell overrides
import '@oluso/admin-ui/styles';
import './index.css';

// Import the Admin UI library
import { AdminApp, setApiBaseUrl, apiClient } from '@oluso/admin-ui';

// Import enterprise plugins
import { createSamlPlugin } from '@oluso/saml-ui';
import { createFido2Plugin } from '@oluso/fido2-ui';
import { createScimPlugin } from '@oluso/scim-ui';
import { createTelemetryPlugin } from '@oluso/telemetry-ui';
import {createLdapPlugin} from "@oluso/ldap-ui";

// Runtime config (supports Cloudflare Pages env vars)
import { config } from './config';

// Configure API base URL
const serverBaseUrl = config.serverUrl;
const apiBaseUrl = config.apiUrl;
setApiBaseUrl(apiBaseUrl);

// Create enterprise plugins
const plugins = [
  createSamlPlugin({ apiClient }),
  createFido2Plugin({ apiClient }),
  createScimPlugin({ apiClient, serverBaseUrl }),
  createTelemetryPlugin({ apiClient }),
  createLdapPlugin({ apiClient }),
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
