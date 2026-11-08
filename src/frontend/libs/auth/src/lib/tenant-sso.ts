/**
 * Helpers for the tenant-aware SSO landing flow shared across host portals.
 *
 * The "Login with SSO" affordance on the landing page resolves a tenant by
 * identifier and persists its issuer URI to sessionStorage. The host's OIDC
 * config picks that up at module load (next page navigation) so the OIDC
 * authority points at the tenant's identity-server subdomain instead of the
 * platform default. After resolve + persist, the landing reloads with
 * `pendingAuthorize` set so the post-load handler can call authorize() with
 * the right authority and the tenant's domain_hint / policy / ui_mode.
 */

const KEY_IDENTIFIER = 'oluso.tenant.identifier';
const KEY_ISSUER_URI = 'oluso.tenant.issuerUri';
const KEY_DEFAULT_IDP_SCHEME = 'oluso.tenant.defaultIdpScheme';
const KEY_DEFAULT_SIGN_IN_POLICY_ID = 'oluso.tenant.defaultSignInPolicyId';
const KEY_DEFAULT_SSO_UI_MODE = 'oluso.tenant.defaultSsoUiMode';
const KEY_PENDING_AUTHORIZE = 'oluso.tenant.pendingAuthorize';

export interface TenantSso {
  identifier: string;
  issuerUri: string;
  defaultIdpScheme?: string;
  defaultSignInPolicyId?: string;
  defaultSsoUiMode?: 'Journey' | 'Standalone';
}

/**
 * Extract tenant identifier from hostname subdomain.
 * e.g., "acme.auth.example.com" → "acme"
 * Returns undefined for localhost, IP addresses, or hostnames with fewer than 3 parts.
 */
export function resolveTenantFromHostname(): string | undefined {
  if (typeof window === 'undefined') return undefined;
  const host = window.location.hostname;
  if (host === 'localhost' || host === '127.0.0.1') return undefined;
  if (/^\d{1,3}(\.\d{1,3}){3}$/.test(host)) return undefined;
  const parts = host.split('.');
  if (parts.length < 3) return undefined;
  return parts[0];
}

/**
 * Returns the tenant SSO authority persisted in sessionStorage, or the given
 * default. Safe in SSR / non-browser contexts.
 */
export function resolveTenantSsoAuthority(defaultAuthority: string): string {
  if (typeof sessionStorage === 'undefined') return defaultAuthority;
  return sessionStorage.getItem(KEY_ISSUER_URI) || defaultAuthority;
}

/**
 * Returns the tenant identifier, issuer URI, and any persisted SSO defaults,
 * or null if none is set.
 */
export function readTenantSso(): TenantSso | null {
  if (typeof sessionStorage === 'undefined') return null;
  const identifier = sessionStorage.getItem(KEY_IDENTIFIER);
  const issuerUri = sessionStorage.getItem(KEY_ISSUER_URI);
  if (!identifier || !issuerUri) return null;

  const uiModeRaw = sessionStorage.getItem(KEY_DEFAULT_SSO_UI_MODE);
  const uiMode = uiModeRaw === 'Journey' || uiModeRaw === 'Standalone' ? uiModeRaw : undefined;

  return {
    identifier,
    issuerUri,
    defaultIdpScheme: sessionStorage.getItem(KEY_DEFAULT_IDP_SCHEME) ?? undefined,
    defaultSignInPolicyId: sessionStorage.getItem(KEY_DEFAULT_SIGN_IN_POLICY_ID) ?? undefined,
    defaultSsoUiMode: uiMode,
  };
}

/**
 * Persists the tenant SSO context resolved by the landing page. Set
 * `markPendingAuthorize` so the post-reload landing handler kicks off
 * authorize() with the tenant's authority + the stored hints.
 *
 * Wipes angular-auth-oidc-client's cached well-known + state on the way
 * out — the cache is keyed by config id, not authority, so switching
 * tenants would otherwise reuse the previous authority's discovery doc
 * and throw issuer-mismatch on the next authorize call.
 */
export function writeTenantSso(sso: TenantSso, markPendingAuthorize = false): void {
  if (typeof sessionStorage === 'undefined') return;

  clearOidcLibraryCache();

  sessionStorage.setItem(KEY_IDENTIFIER, sso.identifier);
  sessionStorage.setItem(KEY_ISSUER_URI, sso.issuerUri);

  if (sso.defaultIdpScheme) {
    sessionStorage.setItem(KEY_DEFAULT_IDP_SCHEME, sso.defaultIdpScheme);
  } else {
    sessionStorage.removeItem(KEY_DEFAULT_IDP_SCHEME);
  }

  if (sso.defaultSignInPolicyId) {
    sessionStorage.setItem(KEY_DEFAULT_SIGN_IN_POLICY_ID, sso.defaultSignInPolicyId);
  } else {
    sessionStorage.removeItem(KEY_DEFAULT_SIGN_IN_POLICY_ID);
  }

  if (sso.defaultSsoUiMode) {
    sessionStorage.setItem(KEY_DEFAULT_SSO_UI_MODE, sso.defaultSsoUiMode);
  } else {
    sessionStorage.removeItem(KEY_DEFAULT_SSO_UI_MODE);
  }

  if (markPendingAuthorize) {
    sessionStorage.setItem(KEY_PENDING_AUTHORIZE, '1');
  }
}

function clearOidcLibraryCache(): void {
  if (typeof sessionStorage === 'undefined') return;
  // angular-auth-oidc-client bundles a config's entire cache (well-known
  // endpoints, authn result, in-flight state, user data) into a single
  // sessionStorage entry keyed "{configIndex}-{clientId}" (e.g. "0-admin-portal").
  // Match that shape: leading digits, dash, anything else. App-owned keys
  // here are namespaced "oluso.tenant.*" and never collide.
  for (const key of Object.keys(sessionStorage)) {
    if (/^\d+-/.test(key)) {
      sessionStorage.removeItem(key);
    }
  }
}

/**
 * Build the customParams object to pass to oidcSecurityService.authorize()
 * from a remembered tenant SSO. Returns undefined if there's nothing worth
 * passing. Callers feed this into the `customParams` field of the authorize
 * call so domain_hint / policy / ui_mode reach /connect/authorize.
 */
export function tenantSsoAuthorizeParams(
  sso: TenantSso,
): Record<string, string> | undefined {
  const params: Record<string, string> = {};
  if (sso.defaultIdpScheme) params['domain_hint'] = sso.defaultIdpScheme;
  if (sso.defaultSignInPolicyId) params['policy'] = sso.defaultSignInPolicyId;
  if (sso.defaultSsoUiMode) params['ui_mode'] = sso.defaultSsoUiMode;
  return Object.keys(params).length ? params : undefined;
}

/**
 * Returns true (and clears the flag) when a pending authorize call was queued
 * by the landing's SSO submit. Use on landing init to auto-trigger
 * oidcSecurityService.authorize() after a reload picked up the new authority.
 */
export function consumePendingAuthorize(): boolean {
  if (typeof sessionStorage === 'undefined') return false;
  if (sessionStorage.getItem(KEY_PENDING_AUTHORIZE) !== '1') return false;
  sessionStorage.removeItem(KEY_PENDING_AUTHORIZE);
  return true;
}

/**
 * Removes all persisted tenant SSO state. Call from logout flows and the
 * "Use a different organisation" affordance on the landing.
 *
 * Also clears angular-auth-oidc-client's sessionStorage cache (well-known
 * discovery doc, in-flight state) — those entries are keyed by config id,
 * not by authority URL, so without wiping them a previously-cached
 * discovery for a different tenant authority survives the switch and
 * throws an "issuer mismatch" on the next authorize call.
 */
export function clearTenantSso(): void {
  if (typeof sessionStorage === 'undefined') return;
  sessionStorage.removeItem(KEY_IDENTIFIER);
  sessionStorage.removeItem(KEY_ISSUER_URI);
  sessionStorage.removeItem(KEY_DEFAULT_IDP_SCHEME);
  sessionStorage.removeItem(KEY_DEFAULT_SIGN_IN_POLICY_ID);
  sessionStorage.removeItem(KEY_DEFAULT_SSO_UI_MODE);
  sessionStorage.removeItem(KEY_PENDING_AUTHORIZE);
  clearOidcLibraryCache();
}
