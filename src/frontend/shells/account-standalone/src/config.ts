// Runtime configuration for Oluso Account
// In production, window.__OLUSO_CONFIG__ is set by /config.js (generated at deploy time)
// In development, falls back to Vite env vars

export interface OlusoConfig {
  serverUrl: string;
  apiUrl: string;
}

declare global {
  interface Window {
    __OLUSO_CONFIG__?: Partial<OlusoConfig>;
  }
}

const runtimeConfig = window.__OLUSO_CONFIG__ ?? {};

const serverUrl = runtimeConfig.serverUrl
  ?? import.meta.env.VITE_SERVER_URL
  ?? import.meta.env.VITE_OIDC_AUTHORITY
  ?? 'http://localhost:5050';

export const config: OlusoConfig = {
  serverUrl,
  apiUrl: runtimeConfig.apiUrl
    ?? import.meta.env.VITE_API_BASE_URL
    ?? serverUrl,
};
