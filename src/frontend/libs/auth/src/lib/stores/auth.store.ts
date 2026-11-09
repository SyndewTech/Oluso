import { Injectable, inject, signal, computed } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { AuthUser } from '../models/auth-user.model';
import { OrganizationContext } from '../models/organization-context.model';
import { AuthState } from '../models/auth-state.model';
import { AuthProvider } from '../auth/auth-provider';
import { tenantOfToken } from '../auth/renew-aware-auth-provider';

const STORAGE_KEY = 'oluso_auth';

/**
 * Auth Store
 *
 * Signal-based authentication state management.
 * This mirrors the Zustand authSlice from the React codebase.
 *
 * Features:
 * - Persistent state via localStorage
 * - Tenant/organization context
 * - Super admin support
 */
@Injectable({
  providedIn: 'root',
})
export class AuthStore {
  private readonly authProvider = inject(AuthProvider);

  private readonly _user = signal<AuthUser | null>(null);
  private readonly _accessToken = signal<string | null>(null);
  private readonly _refreshToken = signal<string | null>(null);
  private readonly _idToken = signal<string | null>(null);
  private readonly _isLoading = signal<boolean>(false);
  private readonly _error = signal<string | null>(null);
  private readonly _currentTenantId = signal<string | null>(null);
  /** True once the user has picked a tenant in the UI; only then do requests carry X-Tenant-ID. */
  private readonly _tenantSwitched = signal<boolean>(false);
  private readonly _currentOrganization = signal<OrganizationContext | null>(null);
  private readonly _isSuperAdmin = signal<boolean>(false);
  private readonly _hasHydrated = signal<boolean>(false);

  /** Read-only computed state */
  readonly user = this._user.asReadonly();
  readonly accessToken = this._accessToken.asReadonly();
  readonly refreshToken = this._refreshToken.asReadonly();
  readonly idToken = this._idToken.asReadonly();
  readonly isLoading = this._isLoading.asReadonly();
  readonly error = this._error.asReadonly();
  readonly currentTenantId = this._currentTenantId.asReadonly();
  readonly tenantSwitched = this._tenantSwitched.asReadonly();
  readonly currentOrganization = this._currentOrganization.asReadonly();
  readonly isSuperAdmin = this._isSuperAdmin.asReadonly();
  readonly hasHydrated = this._hasHydrated.asReadonly();

  readonly isAuthenticated = toSignal(this.authProvider.isAuthenticated$, { initialValue: false });

  /** Full state snapshot */
  readonly state = computed<AuthState>(() => ({
    user: this._user(),
    accessToken: this._accessToken(),
    refreshToken: this._refreshToken(),
    idToken: this._idToken(),
    isAuthenticated: this.isAuthenticated(),
    isLoading: this._isLoading(),
    error: this._error(),
    currentTenantId: this._currentTenantId(),
    currentOrganization: this._currentOrganization(),
    isSuperAdmin: this._isSuperAdmin(),
  }));

  constructor() {
    this.hydrate();
    // Auth-state clearing is handled explicitly by the host portal:
    //   - /logout page's ngOnInit (post-redirect cleanup after oidcService.logoff)
    //   - AdminAuthService.redirectToLogin() (silent-renew failure path)
    //   - AdminAuthService.initialize() (callback / fresh-load cleanup)
    // An automatic isAuthenticated$ → logout() subscription used to live here,
    // but its synchronous 8-signal-write CD cascade fired during
    // resetAuthorizationData() prevented the browser from committing the
    // cross-origin redirect to /connect/endsession.
  }

  /**
   * Set user and tokens after successful login.
   * Extracts tenant_id from the JWT access token if present.
   */
  login(
    user: AuthUser,
    accessToken: string,
    refreshToken?: string,
    idToken?: string
  ): void {
    this._user.set(user);
    this._accessToken.set(accessToken);
    this._refreshToken.set(refreshToken ?? null);
    this._idToken.set(idToken ?? null);
    this._error.set(null);

    this.alignTenantWithToken(accessToken);

    this.persist();
  }

  /**
   * Makes the current tenant the one the token is for. A switch mints a token for its target, so a current
   * tenant naming any other was left by an earlier session (or another app on the origin): it is replaced, and
   * the session counts as not switched, so requests carry no X-Tenant-ID.
   */
  alignTenantWithToken(accessToken: string): void {
    const tokenTenantId = tenantOfToken(accessToken);
    if (this._currentTenantId() !== tokenTenantId) {
      this._currentTenantId.set(tokenTenantId);
      this._tenantSwitched.set(false);
      this.persist();
    }
  }

  /**
   * Clear authentication state on logout
   */
  logout(): void {
    this._user.set(null);
    this._accessToken.set(null);
    this._refreshToken.set(null);
    this._idToken.set(null);
    this._currentTenantId.set(null);
    this._tenantSwitched.set(false);
    this._currentOrganization.set(null);
    this._isSuperAdmin.set(false);
    this._error.set(null);
    this.clearStorage();
  }

  /**
   * Update user information
   */
  setUser(user: AuthUser | null): void {
    this._user.set(user);
    this.persist();
  }

  /**
   * Update access token
   */
  setAccessToken(token: string | null): void {
    this._accessToken.set(token);
    this.persist();
  }

  /**
   * Update refresh token
   */
  setRefreshToken(token: string | null): void {
    this._refreshToken.set(token);
    this.persist();
  }

  /**
   * Set current tenant ID (for super admin)
   */
  setCurrentTenantId(tenantId: string | null): void {
    this._currentTenantId.set(tenantId);
    this._tenantSwitched.set(tenantId !== null);
    this.persist();
  }

  /**
   * Adopt a tenant switch: the new context and the token minted for it.
   *
   * Both move together deliberately. Roles and permissions are per-tenant, so a
   * moment where the header names one tenant and the token carries another
   * either over-grants or under-grants — and a persist() between the two writes
   * would make that state survive a reload.
   */
  switchTenant(tenantId: string, accessToken: string): void {
    this._currentTenantId.set(tenantId);
    this._tenantSwitched.set(true);
    this._accessToken.set(accessToken);
    this.persist();
  }

  /**
   * Set current organization context
   */
  setCurrentOrganization(organization: OrganizationContext | null): void {
    this._currentOrganization.set(organization);
    this.persist();
  }

  /**
   * Set super admin status
   */
  setSuperAdmin(isSuperAdmin: boolean): void {
    this._isSuperAdmin.set(isSuperAdmin);
    this.persist();
  }

  /**
   * Set loading state
   */
  setLoading(isLoading: boolean): void {
    this._isLoading.set(isLoading);
  }

  /**
   * Set error message
   */
  setError(error: string | null): void {
    this._error.set(error);
  }

  /**
   * Hydrate state from localStorage
   */
  private hydrate(): void {
    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      if (stored) {
        const data = JSON.parse(stored);
        this._user.set(data.user ?? null);
        this._accessToken.set(data.accessToken ?? null);
        this._refreshToken.set(data.refreshToken ?? null);
        this._idToken.set(data.idToken ?? null);
        this._currentTenantId.set(data.currentTenantId ?? null);
        this._tenantSwitched.set(data.tenantSwitched === true);
        this._currentOrganization.set(data.currentOrganization ?? null);
        this._isSuperAdmin.set(data.isSuperAdmin ?? false);
      }
    } catch {
      // Ignore hydration errors
    } finally {
      this._hasHydrated.set(true);
    }
  }

  /**
   * Persist state to localStorage
   */
  private persist(): void {
    try {
      const data = {
        user: this._user(),
        accessToken: this._accessToken(),
        refreshToken: this._refreshToken(),
        idToken: this._idToken(),
        currentTenantId: this._currentTenantId(),
        tenantSwitched: this._tenantSwitched(),
        currentOrganization: this._currentOrganization(),
        isSuperAdmin: this._isSuperAdmin(),
      };
      localStorage.setItem(STORAGE_KEY, JSON.stringify(data));
    } catch {
      // Ignore persistence errors
    }
  }

  /**
   * Clear localStorage
   */
  private clearStorage(): void {
    try {
      localStorage.removeItem(STORAGE_KEY);
    } catch {
      // Ignore errors
    }
  }
}
