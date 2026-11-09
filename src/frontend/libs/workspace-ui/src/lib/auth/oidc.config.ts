import { PassedInitialConfig, LogLevel } from 'angular-auth-oidc-client';
import { readTenantSso, resolveTenantSsoAuthority, tenantSsoAuthorizeParams } from '@syndew/oluso-auth';

export interface WorkspaceRuntimeConfig {
  serverUrl?: string;
  apiUrl?: string;
  /**
   * Host-level URL of the Account app (profile, password, security). Same
   * key the admin shell uses. A tenant can override it under Workspace Apps;
   * unset, the workspace assumes the app is served at /account on its origin.
   */
  accountUrl?: string;
  /** Host-level URL of the Admin app; same override rules as accountUrl. */
  adminUrl?: string;
}

// Get runtime config (set by shell's config.js). The workspace-specific
// global wins; the shared one is accepted so a host can ship a single script.
type RuntimeConfigWindow = Window & {
  __OLUSO_WORKSPACE_CONFIG__?: WorkspaceRuntimeConfig;
  __OLUSO_CONFIG__?: WorkspaceRuntimeConfig;
};
const runtimeWindow = typeof window !== 'undefined' ? (window as RuntimeConfigWindow) : undefined;
const runtimeConfig: WorkspaceRuntimeConfig =
  runtimeWindow?.__OLUSO_WORKSPACE_CONFIG__ || runtimeWindow?.__OLUSO_CONFIG__ || {};

// Resolve server URL: runtime config > default
export const serverUrl: string = runtimeConfig.serverUrl || 'http://localhost:5050';

// Resolve API URL: runtime config > derived from server URL
export const apiUrl: string = runtimeConfig.apiUrl || serverUrl;

// Host defaults for the companion apps; empty when the host did not set them.
export const accountUrl: string = runtimeConfig.accountUrl || '';
export const adminUrl: string = runtimeConfig.adminUrl || '';

const sso = readTenantSso();
const ssoParams = sso ? tenantSsoAuthorizeParams(sso) : undefined;

/**
 * OIDC Configuration for Workspace Portal.
 *
 * SSO params (domain_hint, policy, ui_mode) are read from sessionStorage
 * at bootstrap so authorize() carries them automatically after an SSO reload.
 */
export const workspaceOidcConfig: PassedInitialConfig = {
  config: {
    authority: resolveTenantSsoAuthority(serverUrl),
    redirectUrl: typeof document !== 'undefined' ? new URL('callback', document.baseURI).href : '',
    postLogoutRedirectUri: typeof document !== 'undefined' ? new URL('logout', document.baseURI).href : '',
    clientId: 'workspace-portal',
    scope: 'openid profile email permissions offline_access',
    responseType: 'code',
    // Renew with the refresh token before the access token expires. With
    // silentRenew off the library never renews on its own and a reload after
    // the access token lapses lands on the sign-in page, however long the
    // refresh token is valid for. Refreshed ID tokens carry no nonce, so the
    // nonce check is skipped for them.
    silentRenew: true,
    useRefreshToken: true,
    ignoreNonceAfterRefresh: true,
    triggerRefreshWhenIdTokenExpired: false,
    renewTimeBeforeTokenExpiresInSeconds: 60,
    // The callback page decides where the user lands (back to the page their session ended on, else the
    // dashboard); left to the library, it navigates to its post-login route '/' after every sign-in.
    triggerAuthorizationResultEvent: true,
    logLevel: LogLevel.Warn,
    secureRoutes: [serverUrl, '/api'],
    ...(ssoParams ? { customParamsAuthRequest: ssoParams } : {}),
  },
};
