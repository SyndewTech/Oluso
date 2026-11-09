import { Injectable, inject, signal, computed, DestroyRef, Injector } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { Subject, firstValueFrom, filter, switchMap } from 'rxjs';
import { OidcSecurityService, PublicEventsService, EventTypes } from 'angular-auth-oidc-client';
import { AuthStore, checkAuthRenewingSession, rememberReturnUrl } from '@syndew/oluso-auth';
import { PermissionsService, FeatureService } from '@syndew/oluso-ui-core';
import { PermissionService } from './permission.service';
import { ApiService } from './api.service';

/**
 * Admin authentication service using OIDC.
 * Wraps OidcSecurityService, populates AuthStore with user info from tokens,
 * and loads permissions via PermissionService.
 *
 * Also monitors silent renew lifecycle events — if a background token refresh
 * fails (e.g. refresh token expired) and there is no way to recover, the user
 * is redirected back to the login page automatically.
 */
@Injectable({
  providedIn: 'root',
})
export class AdminAuthService {
  private readonly oidcService = inject(OidcSecurityService);
  private readonly eventService = inject(PublicEventsService);
  private readonly authStore = inject(AuthStore);
  private readonly permissionsService = inject(PermissionsService);
  private readonly permissionService = inject(PermissionService);
  private readonly apiService = inject(ApiService);
  private readonly featureService = inject(FeatureService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);

  /** Background /permissions/my reloads; a newer request supersedes an in-flight one. */
  private readonly permissionRefresh$ = new Subject<void>();

  private readonly _isInitialized = signal(false);
  readonly isInitialized = this._isInitialized.asReadonly();
  readonly isAuthenticated = computed(() => this.authStore.isAuthenticated());

  /** Tracks consecutive silent renew failures to avoid spamming retries */
  private silentRenewFailCount = 0;
  private static readonly MAX_SILENT_RENEW_RETRIES = 2;

  /**
   * Initialize auth by checking OIDC state and loading user data.
   * Called from APP_INITIALIZER.
   */
  async initialize(): Promise<void> {
    if (this._isInitialized()) return;

    // Skip auth check on logout page — the user just signed out,
    // checkAuth() would trigger silent renew against a dead session.
    const path = window.location.pathname;
    if (path === '/logout' || path.endsWith('/logout')) {
      this.authStore.logout();
      this._isInitialized.set(true);
      return;
    }

    try {
      // An expired access token is renewed with the refresh token here, not by sending the user to sign in.
      const loginResponse = await firstValueFrom(checkAuthRenewingSession(this.oidcService));

      if (loginResponse.isAuthenticated) {
        this.updateAuthState(loginResponse.accessToken, loginResponse.idToken);

        try {
          await firstValueFrom(this.fetchPermissions());
        } catch (error) {
          console.error('Failed to fetch permissions:', error);
        }

        // Load tenant features for module nav visibility
        try {
          const featuresResponse = await firstValueFrom(
            this.apiService.get<{ features: Record<string, { enabled: boolean }> }>('/features')
          );
          if (featuresResponse?.features) {
            const featureMap: Record<string, boolean> = {};
            for (const [key, value] of Object.entries(featuresResponse.features)) {
              featureMap[key] = value.enabled;
            }
            this.featureService.setFeatures(featureMap);
          }
        } catch (error) {
          console.error('Failed to fetch features:', error);
        }
      } else {
        // Clear stale localStorage state — OIDC session is gone
        this.authStore.logout();
        this.permissionsService.clear();
      }

      this.permissionRefresh$
        .pipe(
          switchMap(() => this.fetchPermissions()),
          takeUntilDestroyed(this.destroyRef)
        )
        .subscribe();

      // Subscribe to token renewal events to keep AuthStore in sync
      this.oidcService.getAccessToken()
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe((token) => {
          if (token && this._isInitialized() && token !== this.authStore.accessToken()) {
            // Reset failure count on successful token delivery
            this.silentRenewFailCount = 0;
            this.updateAuthState(token);
            this.permissionRefresh$.next();
          }
        });

      // A tenant switch changes which permissions apply: adopt the new
      // identity (dropping the old tenant's set) and reload.
      toObservable(this.authStore.currentTenantId, { injector: this.injector })
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe((tenantId) => {
          const userId = this.authStore.user()?.id;
          if (userId && this.permissionsService.useIdentity(userId, tenantId)) {
            this.permissionRefresh$.next();
          }
        });

      // Monitor silent renew failures — track consecutive failures and redirect
      // when recovery is impossible. We do NOT call forceRefreshSession() here
      // because that races with the library's own silent renew, creating nonce
      // conflicts. The interceptor handles 401 retries independently.
      this.eventService.registerForEvents()
        .pipe(
          filter((event) => event.type === EventTypes.SilentRenewFailed),
          takeUntilDestroyed(this.destroyRef)
        )
        .subscribe((event) => {
          this.handleSilentRenewFailure(event.value);
        });
    } catch (error) {
      console.error('Failed to initialize admin auth:', error);
      this.permissionsService.clear();
      // Most common cause: the current URL still carries a `?code=...&state=...`
      // callback pair from a previous flow whose stored state has since been
      // cleared (back button, stale tab, multi-tab race). The library can't
      // complete the code exchange, but the user may still have a valid
      // cookie/refresh session. Strip the callback params and route to
      // dashboard; if there is no session, the guard sends them to landing.
      this.recoverFromStaleCallback();
    } finally {
      this._isInitialized.set(true);
    }
  }

  /**
   * Strip OIDC callback query params (`code`, `state`, `session_state`,
   * `iss`, and any `error*` pair) from the current URL and navigate to
   * /dashboard. Called when checkAuth() throws — typically because a
   * stale callback URL was loaded after storage was cleared.
   */
  private recoverFromStaleCallback(): void {
    const url = new URL(window.location.href);
    const callbackParams = ['code', 'state', 'session_state', 'iss', 'error', 'error_description'];
    let touched = false;
    for (const p of callbackParams) {
      if (url.searchParams.has(p)) {
        url.searchParams.delete(p);
        touched = true;
      }
    }
    if (touched) {
      window.history.replaceState({}, '', url.pathname + (url.search ? url.search : '') + url.hash);
    }
    // Navigate to dashboard either way — the auth guard will redirect to
    // landing if no session exists.
    this.router.navigate(['/dashboard']).catch(() => {
      // Defensive: if navigation fails (e.g. no /dashboard route), do nothing.
    });
  }

  /**
   * Trigger OIDC logout flow.
   */
  logout(): void {
    this.authStore.logout();
    this.permissionsService.clear();
    this.oidcService.logoff().subscribe();
  }

  /**
   * Handle silent renew failure — track consecutive failures and redirect when
   * the session is unrecoverable. Does NOT call forceRefreshSession() because
   * that creates a competing auth flow with a new nonce, which races with the
   * library's own renew mechanism and causes "incorrect nonce" errors.
   * The interceptor's 401 retry logic handles individual request recovery.
   */
  private handleSilentRenewFailure(error: unknown): void {
    this.silentRenewFailCount++;
    console.warn(
      `Silent renew failed (attempt ${this.silentRenewFailCount}/${AdminAuthService.MAX_SILENT_RENEW_RETRIES}):`,
      error
    );

    if (this.silentRenewFailCount > AdminAuthService.MAX_SILENT_RENEW_RETRIES) {
      this.redirectToLogin();
    }
  }

  /**
   * Redirect to login when the session is unrecoverable.
   */
  private redirectToLogin(): void {
    console.warn('Session unrecoverable — redirecting to login');
    rememberReturnUrl();
    this.authStore.logout();
    this.permissionsService.clear();
    this.oidcService.authorize();
  }

  /**
   * Update AuthStore and permissions from an access token.
   */
  private updateAuthState(accessToken: string, idToken?: string): void {
    // Silent renewals only deliver a new access token; keep the ID token we
    // already hold so profile claims (picture) survive the refresh.
    const effectiveIdToken = idToken ?? this.authStore.idToken() ?? undefined;
    const user = this.extractUserFromToken(accessToken, effectiveIdToken);
    this.authStore.login(user, accessToken, undefined, effectiveIdToken);

    // Check for super admin
    if (user.roles?.includes('SuperAdmin')) {
      this.authStore.setSuperAdmin(true);
    }

    // Set organization context
    if (user.organizationIds?.length && user.organizationRoles) {
      const adminOrgId = user.organizationIds.find(
        (id) => user.organizationRoles?.[id] === 'owner' || user.organizationRoles?.[id] === 'admin'
      ) ?? user.organizationIds[0];

      this.authStore.setCurrentOrganization({
        id: adminOrgId,
        name: adminOrgId,
        role: user.organizationRoles[adminOrgId],
      });
    }

    // Scope permissions to this user + tenant. A new identity starts without
    // permissions until /permissions/my (or its fallback) supplies them; the
    // same identity keeps its current set across token renewals.
    this.permissionsService.useIdentity(user.id, this.authStore.currentTenantId());
  }

  /**
   * Load /permissions/my for the current identity. If the API is unreachable
   * and there is no cached set for this identity, fall back to the current
   * access token's claims — but never replace a set already held for it.
   */
  private fetchPermissions() {
    return this.permissionService.getMyPermissions(() => {
      if (this.permissionsService.isAuthoritative()) return;
      const token = this.authStore.accessToken();
      if (!token) return;
      const claims = this.extractUserFromToken(token);
      if (claims.id !== this.authStore.user()?.id) return;
      this.permissionsService.initFromLoginResponse(claims.permissions ?? [], claims.roles ?? []);
    });
  }

  /**
   * Extract user information from a JWT access token.
   */
  private extractUserFromToken(token: string, idToken?: string): import('@syndew/oluso-auth').AuthUser {
    try {
      const payload = decodeJwtPayload(token);
      // Profile claims (picture, given/family name) are released on the ID
      // token under the profile scope; the access token may not carry them.
      const identity: TokenClaims = idToken ? decodeJwtPayload(idToken) : {};

      return {
        id: payload.sub || '',
        email: payload.email || identity.email || '',
        userName: payload.preferred_username || payload.name || identity.preferred_username || '',
        displayName: payload.name || identity.name || payload.preferred_username || '',
        firstName: payload.given_name || identity.given_name || '',
        lastName: payload.family_name || identity.family_name || '',
        picture: identity.picture || payload.picture || undefined,
        emailVerified: payload.email_verified ?? false,
        roles: payload.role ? (Array.isArray(payload.role) ? payload.role : [payload.role]) : [],
        permissions: payload.permissions ? (Array.isArray(payload.permissions) ? payload.permissions : [payload.permissions]) : [],
        organizationIds: payload.org_ids ? (Array.isArray(payload.org_ids) ? payload.org_ids : [payload.org_ids]) : [],
        organizationRoles: parseOrgRoles(payload.org_roles),
      };
    } catch {
      return { id: '', email: '' };
    }
  }
}

/**
 * org_roles arrives as a JSON-encoded *string*, not an object: the backend
 * pre-serializes the dictionary and TokenService writes plain strings without
 * a JSON value type. Indexing it directly yields undefined, which silently made
 * the owner/admin lookup below never match.
 */
function parseOrgRoles(value: unknown): Record<string, string> {
  if (!value) return {};
  if (typeof value === 'object') return value as Record<string, string>;

  if (typeof value === 'string') {
    try {
      const parsed = JSON.parse(value);
      return typeof parsed === 'object' && parsed !== null ? parsed : {};
    } catch {
      return {};
    }
  }

  return {};
}

interface TokenClaims {
  sub?: string;
  email?: string;
  email_verified?: boolean;
  name?: string;
  preferred_username?: string;
  given_name?: string;
  family_name?: string;
  picture?: string;
  role?: string | string[];
  permissions?: string | string[];
  org_ids?: string | string[];
  org_roles?: unknown;
}

function decodeJwtPayload(token: string): TokenClaims {
  return JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/'))) as TokenClaims;
}
