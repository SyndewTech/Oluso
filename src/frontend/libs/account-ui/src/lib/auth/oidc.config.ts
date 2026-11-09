import { PassedInitialConfig, LogLevel } from 'angular-auth-oidc-client';
import { readTenantSso, resolveTenantSsoAuthority, tenantSsoAuthorizeParams } from '@syndew/oluso-auth';

export interface OlusoRuntimeConfig {
  serverUrl?: string;
  apiUrl?: string;
  /**
   * Public URL of the Workspace app. When set, the account sidebar shows a
   * "Back to workspace" link; unset hides it (account-only deployments).
   */
  workspaceUrl?: string;
}

// Get runtime config (set by shell's config.js)
type RuntimeConfigWindow = Window & { __OLUSO_CONFIG__?: OlusoRuntimeConfig };
const runtimeConfig: OlusoRuntimeConfig =
  (typeof window !== 'undefined' && (window as RuntimeConfigWindow).__OLUSO_CONFIG__) || {};

// Resolve server URL: runtime config > default
export const serverUrl: string = runtimeConfig.serverUrl || 'http://localhost:5050';

// Resolve API URL: runtime config > derived from server URL
export const apiUrl: string = runtimeConfig.apiUrl || `${serverUrl}/api/account`;

// Resolve workspace app URL: runtime config only — empty string hides the link
export const workspaceUrl: string = runtimeConfig.workspaceUrl || '';

const sso = readTenantSso();
const ssoParams = sso ? tenantSsoAuthorizeParams(sso) : undefined;

export const oidcConfig: PassedInitialConfig = {
  config: {
    authority: resolveTenantSsoAuthority(serverUrl),
    redirectUrl: typeof document !== 'undefined' ? new URL('callback', document.baseURI).href : '',
    postLogoutRedirectUri: typeof document !== 'undefined' ? new URL('logout', document.baseURI).href : '',
    clientId: 'account-ui',
    // `permissions` scope is required for the token to carry the user's
    // permissions claim — plugin affordances inside the Account UI
    // permission-gate the same way the other portals do.
    scope: 'openid profile email permissions offline_access',
    responseType: 'code',
    // Refresh-token-based renewal (offline_access). The iframe-based silent
    // renew path was firing against the wrong tenant authority and 401-ing
    // calls like /api/account/ciba/pending; using the refresh_token grant
    // through the auth interceptor avoids the iframe round-trip entirely.
    // Renew with the refresh token before the access token expires. With
    // silentRenew off the library never renews on its own and a reload after
    // the access token lapses lands on the sign-in page, however long the
    // refresh token is valid for. Refreshed ID tokens carry no nonce, so the
    // nonce check is skipped for them.
    silentRenew: true,
    useRefreshToken: true,
    ignoreNonceAfterRefresh: true,
    renewTimeBeforeTokenExpiresInSeconds: 30,
    logLevel: LogLevel.Warn,
    secureRoutes: [serverUrl, '/api'],
    ...(ssoParams ? { customParamsAuthRequest: ssoParams } : {}),
  },
};
