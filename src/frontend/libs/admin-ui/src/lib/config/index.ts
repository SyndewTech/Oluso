/**
 * Runtime configuration for Oluso Admin UI
 * In production, window.__OLUSO_CONFIG__ is set by the shell's /config.js
 * In development, falls back to environment variables or defaults
 */

export interface OlusoRuntimeConfig {
  serverUrl?: string;
  apiUrl?: string;
  /**
   * Public URL of the Account app where end users self-serve profile,
   * password, and security settings. The admin shell shows a "My account"
   * affordance that links to this URL when set. Leave unset to hide it
   * (single-app deployments where the admin and account UIs share an
   * origin can leave this blank).
   */
  accountUrl?: string;
  /**
   * Public URL of the Workspace app. When set, the admin user menu shows a
   * "Back to workspace" link so admins can hop back without retyping it.
   */
  workspaceUrl?: string;
}

// Get runtime config (set by shell's config.js)
type RuntimeConfigWindow = Window & { __OLUSO_CONFIG__?: OlusoRuntimeConfig };
const runtimeConfig: OlusoRuntimeConfig =
  (typeof window !== 'undefined' && (window as RuntimeConfigWindow).__OLUSO_CONFIG__) || {};

// Resolve server URL: runtime config > current origin > localhost fallback
export const serverUrl: string =
  runtimeConfig.serverUrl ||
  (typeof window !== 'undefined' ? window.location.origin : 'http://localhost:5050');

// Resolve API URL: runtime config > derived from server URL
export const apiUrl: string = runtimeConfig.apiUrl || `${serverUrl}/api/admin`;

// Resolve account app URL: runtime config only — empty string hides the link
export const accountUrl: string = runtimeConfig.accountUrl || '';

// Resolve workspace app URL: runtime config only — empty string hides the link
export const workspaceUrl: string = runtimeConfig.workspaceUrl || '';

export const config = {
  serverUrl,
  apiUrl,
  accountUrl,
  workspaceUrl,
};

/**
 * Set the API configuration at runtime.
 * This can be called from the app's main.ts or app.config.ts
 */
export function setConfig(newConfig: Partial<OlusoRuntimeConfig>): void {
  if (newConfig.serverUrl) {
    (config as { serverUrl: string }).serverUrl = newConfig.serverUrl;
  }
  if (newConfig.apiUrl) {
    (config as { apiUrl: string }).apiUrl = newConfig.apiUrl;
  }
  if (newConfig.accountUrl !== undefined) {
    (config as { accountUrl: string }).accountUrl = newConfig.accountUrl;
  }
  if (newConfig.workspaceUrl !== undefined) {
    (config as { workspaceUrl: string }).workspaceUrl = newConfig.workspaceUrl;
  }
}
