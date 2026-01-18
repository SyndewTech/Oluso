// Runtime configuration for Oluso Workspace Portal
// In production, window.__OLUSO_CONFIG__ is set by /config.js (generated at deploy time)
// In development, falls back to Vite env vars

export interface OlusoConfig {
  serverUrl: string;
  apiUrl: string;
  clientId: string;
  scopes: string;
}

declare global {
  interface Window {
    __OLUSO_CONFIG__?: Partial<OlusoConfig>;
  }
}

const runtimeConfig = window.__OLUSO_CONFIG__ ?? {};

const serverUrl = runtimeConfig.serverUrl
  ?? import.meta.env.VITE_SERVER_URL
  ?? 'http://localhost:5050';

export const config: OlusoConfig = {
  serverUrl,
  apiUrl: runtimeConfig.apiUrl
    ?? import.meta.env.VITE_API_URL
    ?? serverUrl,
  clientId: runtimeConfig.clientId
    ?? import.meta.env.VITE_OIDC_CLIENT_ID
    ?? 'workspace-portal',
  scopes: runtimeConfig.scopes
    ?? import.meta.env.VITE_OIDC_SCOPES
    ?? 'openid profile email workspace',
};
