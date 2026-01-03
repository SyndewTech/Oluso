import { UserManagerSettings, WebStorageStateStore } from 'oidc-client-ts';

/**
 * Runtime configuration interface
 */
interface OlusoRuntimeConfig {
  serverUrl?: string;
  apiUrl?: string;
}

declare global {
  interface Window {
    __OLUSO_CONFIG__?: OlusoRuntimeConfig;
  }
}

// Get runtime config (set by shell's config.js)
const runtimeConfig = window.__OLUSO_CONFIG__ ?? {};

/**
 * Configuration options for OIDC
 */
export interface OidcConfig {
  authority: string;
  clientId: string;
  redirectUri?: string;
  postLogoutRedirectUri?: string;
  scope?: string;
}

/**
 * Create OIDC configuration from options
 */
export function createOidcConfig(options: OidcConfig): UserManagerSettings {
  return {
    authority: options.authority,
    client_id: options.clientId,
    redirect_uri: options.redirectUri || `${window.location.origin}/callback`,
    post_logout_redirect_uri: options.postLogoutRedirectUri || window.location.origin,
    scope: options.scope || 'openid profile email account',
    response_type: 'code',

    // Use PKCE (enabled by default in oidc-client-ts v3+)
    // S256 is the only secure method

    // Token storage
    userStore: new WebStorageStateStore({ store: window.localStorage }),

    // Automatic token refresh
    automaticSilentRenew: true,

    // Include ID token in silent renew
    includeIdTokenInSilentRenew: true,

    // Load user info from userinfo endpoint
    loadUserInfo: true,

    // Monitor session state
    monitorSession: true,
  };
}

// Resolve server URL: runtime config > env var > localhost fallback
const serverUrl = runtimeConfig.serverUrl
  ?? import.meta.env.VITE_OIDC_AUTHORITY
  ?? import.meta.env.VITE_SERVER_URL
  ?? 'http://localhost:5050';

// Default OIDC configuration from environment variables
export const oidcConfig: UserManagerSettings = {
  authority: serverUrl,
  client_id: import.meta.env.VITE_OIDC_CLIENT_ID || 'account-ui',
  redirect_uri: import.meta.env.VITE_OIDC_REDIRECT_URI || `${window.location.origin}/callback`,
  post_logout_redirect_uri: import.meta.env.VITE_OIDC_POST_LOGOUT_REDIRECT_URI || window.location.origin,
  scope: import.meta.env.VITE_OIDC_SCOPE || 'openid profile email account',
  response_type: 'code',

  // Token storage
  userStore: new WebStorageStateStore({ store: window.localStorage }),

  automaticSilentRenew: true,

  includeIdTokenInSilentRenew: true,

  // Load user info from userinfo endpoint
  loadUserInfo: true,

  // Monitor session state
  monitorSession: true,
};

// API base URL: runtime config > env var > server URL
export const apiBaseUrl = runtimeConfig.apiUrl
  ?? import.meta.env.VITE_API_BASE_URL
  ?? serverUrl;
