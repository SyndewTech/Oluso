/**
 * Runtime configuration for Workspace Portal
 *
 * In production, the shell app sets window.__OLUSO_CONFIG__ via /config.js
 * In development, the shell uses Vite env vars to populate the config
 *
 * This library should NOT use import.meta.env directly - the shell handles that.
 */

export interface WorkspaceConfig {
  serverUrl: string;
  apiUrl: string;
  clientId: string;
  scopes: string;
}

declare global {
  interface Window {
    __OLUSO_CONFIG__?: Partial<WorkspaceConfig>;
  }
}

// Default configuration
const defaults: WorkspaceConfig = {
  serverUrl: 'http://localhost:5050',
  apiUrl: 'http://localhost:5050',
  clientId: 'workspace-portal',
  scopes: 'openid profile email workspace',
};

// Runtime configuration (set by shell app)
const runtimeConfig = typeof window !== 'undefined' ? window.__OLUSO_CONFIG__ || {} : {};

// Merged configuration
export const serverUrl = runtimeConfig.serverUrl ?? defaults.serverUrl;
export const apiUrl = runtimeConfig.apiUrl ?? defaults.apiUrl;
export const clientId = runtimeConfig.clientId ?? defaults.clientId;
export const scopes = runtimeConfig.scopes ?? defaults.scopes;

// OIDC Configuration
export const oidcConfig = {
  authority: serverUrl,
  client_id: clientId,
  redirect_uri: typeof window !== 'undefined' ? `${window.location.origin}/callback` : '',
  post_logout_redirect_uri: typeof window !== 'undefined' ? `${window.location.origin}/` : '',
  response_type: 'code',
  scope: scopes,
  automaticSilentRenew: true,
  loadUserInfo: true,
  onSigninCallback: () => {
    // Remove the code and state from the URL after sign-in and navigate to dashboard
    window.history.replaceState({}, document.title, '/');
  },
};

export function createOidcConfig(overrides?: Partial<typeof oidcConfig>) {
  return { ...oidcConfig, ...overrides };
}
