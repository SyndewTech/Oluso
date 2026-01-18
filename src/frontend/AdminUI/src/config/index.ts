/**
 * Runtime configuration for Oluso Admin UI
 * In production, window.__OLUSO_CONFIG__ is set by the shell's /config.js
 * In development, falls back to Vite env vars
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

// Resolve server URL: runtime config > env var > localhost fallback
export const serverUrl = runtimeConfig.serverUrl
  ?? import.meta.env.VITE_SERVER_URL
  ?? 'http://localhost:5050';

// Resolve API URL: runtime config > env var > derived from server URL
export const apiUrl = runtimeConfig.apiUrl
  ?? import.meta.env.VITE_API_URL
  ?? `${serverUrl}/api/admin`;

export const config = {
  serverUrl,
  apiUrl,
};
