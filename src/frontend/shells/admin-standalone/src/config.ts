// Runtime configuration for Oluso Admin
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

export const config: OlusoConfig = {
  serverUrl: runtimeConfig.serverUrl
    ?? import.meta.env.VITE_SERVER_URL
    ?? 'http://localhost:5050',
  apiUrl: runtimeConfig.apiUrl
    ?? import.meta.env.VITE_API_URL
    ?? `${runtimeConfig.serverUrl ?? import.meta.env.VITE_SERVER_URL ?? 'http://localhost:5050'}/api/admin`,
};
