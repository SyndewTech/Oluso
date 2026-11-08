import { PassedInitialConfig, LogLevel } from 'angular-auth-oidc-client';
import { readTenantSso, resolveTenantSsoAuthority, tenantSsoAuthorizeParams } from '@oluso/auth';
import { serverUrl } from '../config';

const sso = readTenantSso();
const ssoParams = sso ? tenantSsoAuthorizeParams(sso) : undefined;

/**
 * OIDC Configuration for Admin Portal.
 *
 * SSO params (domain_hint, policy, ui_mode) are read from sessionStorage
 * at bootstrap so authorize() carries them automatically after an SSO reload.
 *
 * Standalone apps add their own params (ui_locales, tenant, etc.) via
 * provideAdminAuth({ customParamsAuthRequest: { ... } }) — these are
 * merged with SSO params, not replaced.
 */
export const adminOidcConfig: PassedInitialConfig = {
  config: {
    authority: resolveTenantSsoAuthority(serverUrl),
    redirectUrl: typeof document !== 'undefined' ? new URL('callback', document.baseURI).href : '',
    postLogoutRedirectUri: typeof document !== 'undefined' ? new URL('logout', document.baseURI).href : '',
    clientId: 'admin-portal',
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
    disableRefreshIdTokenAuthTimeValidation: true,
    logLevel: LogLevel.Warn,
    secureRoutes: [serverUrl, '/api'],
    ...(ssoParams ? { customParamsAuthRequest: ssoParams } : {}),
  },
};
