import { Injectable, inject, signal, computed, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { firstValueFrom, filter } from 'rxjs';
import { WorkspaceUser } from '../models';
import { WorkspaceApiService } from './workspace-api.service';
import { WorkspaceAppsConfigService } from './workspace-apps-config.service';
import { FeatureService, ModuleSeatService, PermissionsService } from '@syndew/oluso-ui-core';
import { OidcSecurityService, PublicEventsService, EventTypes } from 'angular-auth-oidc-client';
import { AuthStore, checkAuthRenewingSession, rememberReturnUrl } from '@syndew/oluso-auth';

/**
 * Workspace authentication and authorization service.
 * Manages user state and provides permission/feature checking.
 * Monitors OIDC lifecycle events to handle silent renew failures gracefully.
 */
@Injectable({
  providedIn: 'root',
})
export class WorkspaceAuthService {
  private readonly oidcService = inject(OidcSecurityService);
  private readonly eventService = inject(PublicEventsService);
  private readonly apiService = inject(WorkspaceApiService);
  private readonly appsConfig = inject(WorkspaceAppsConfigService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);
  // ui-core's slot/nav/quick-action filter consults FeatureService +
  // ModuleSeatService, and route guards and *uiHasPermission consult
  // PermissionsService — we mirror our user-loaded data into all three so
  // modules gate on the same source without calling WorkspaceAuthService.
  private readonly featureService = inject(FeatureService);
  private readonly seatService = inject(ModuleSeatService);
  private readonly permissionsService = inject(PermissionsService);
  // Holds the tenant tenantInterceptor sends as X-Tenant-ID, persisted in localStorage and shared with admin.
  private readonly authStore = inject(AuthStore);

  // State signals
  private readonly _user = signal<WorkspaceUser | null>(null);
  private readonly _isLoading = signal(true);
  private readonly _isInitialized = signal(false);
  private _initPromise: Promise<void> | null = null;

  // Public computed values
  readonly user = this._user.asReadonly();
  readonly isLoading = this._isLoading.asReadonly();
  readonly isInitialized = this._isInitialized.asReadonly();

  readonly isAuthenticated = computed(() => this._user() !== null);
  readonly isManager = computed(() => this._user()?.isManager ?? false);
  readonly permissions = computed(() => this._user()?.permissions ?? []);
  readonly features = computed(() => this._user()?.features ?? []);
  readonly moduleSeats = computed(() => this._user()?.moduleSeats ?? []);
  readonly displayName = computed(() => this._user()?.displayName ?? '');

  /** Tracks consecutive silent renew failures */
  private silentRenewFailCount = 0;
  private static readonly MAX_SILENT_RENEW_RETRIES = 2;

  /**
   * Initialize the auth service by fetching user profile
   */
  async initialize(): Promise<void> {
    if (this._isInitialized()) return;
    // Dedupe re-entrant callers (APP_INITIALIZER, layout, and route guards via
    // whenReady) onto one in-flight load — otherwise checkAuth can run twice, or
    // a guard reads features before the profile has landed and bounces the user
    // to /unauthorized?reason=feature_unavailable.
    this._initPromise ??= this.runInitialize();
    return this._initPromise;
  }

  /** Resolves once the initial auth + profile load has finished. */
  whenReady(): Promise<void> {
    return this._isInitialized() ? Promise.resolve() : this.initialize();
  }

  private async runInitialize(): Promise<void> {
    this._isLoading.set(true);
    try {
      // Handles both cases:
      // 1. On callback URL: exchanges authorization code for tokens
      // 2. Otherwise: validates existing tokens in storage, renewing an expired access token with the refresh token
      const loginResponse = await firstValueFrom(checkAuthRenewingSession(this.oidcService));

      if (loginResponse.isAuthenticated) {
        this.authStore.alignTenantWithToken(loginResponse.accessToken);
        await this.loadUserProfile();
        await this.loadAppsSettings();
      } else {
        this.authStore.logout();
        this.permissionsService.clear();
      }

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

      // Reset failure count whenever we get a fresh token
      this.oidcService.getAccessToken()
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe((token) => {
          if (token && this._isInitialized()) {
            this.silentRenewFailCount = 0;
          }
        });
    } catch (error) {
      console.error('Failed to initialize workspace auth:', error);
      this.permissionsService.clear();
      // Stale ?code=&state= from a back-button or fresh-tab race —
      // checkAuth() can't complete the code exchange but a valid cookie
      // session may still exist. Clean the URL and route to dashboard;
      // the guard sends them to landing if no session is found.
      this.recoverFromStaleCallback();
    } finally {
      this._isLoading.set(false);
      this._isInitialized.set(true);
    }
  }

  /**
   * Strip OIDC callback query params from the current URL and navigate to
   * /dashboard. Called when checkAuth() throws — typically because a stale
   * callback URL was loaded after storage was cleared.
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
    this.router.navigate(['/dashboard']).catch(() => {
      // Defensive: if /dashboard doesn't exist or guard rejects, leave it.
    });
  }

  /**
   * Load user profile from the workspace /me endpoint, applying any
   * module-provided profile extensions.
   */
  /**
   * Tenant app switcher settings (built-in toggles, custom apps, URL
   * overrides). Best-effort: the defaults still render if this fails.
   */
  private async loadAppsSettings(): Promise<void> {
    try {
      await firstValueFrom(this.appsConfig.loadSettings());
    } catch (error) {
      console.warn('Failed to load workspace apps settings:', error);
    }
  }

  async loadUserProfile(): Promise<void> {
    try {
      const profile = await firstValueFrom(this.apiService.getMyProfile());

      // Derive isManager from a module-provided profile extension if present.
      // The extension key matches the module that populates /me (server-side enricher).
      const managerExtension = profile.extensions?.['hr'] as { isManager?: boolean } | undefined;

      const user: WorkspaceUser = {
        id: profile.id,
        userName: profile.userName,
        email: profile.email || '',
        displayName: profile.displayName,
        firstName: profile.firstName,
        lastName: profile.lastName,
        profilePictureUrl: profile.profilePictureUrl,
        isManager: managerExtension?.isManager ?? false,
        roles: profile.roles || [],
        permissions: profile.permissions || [],
        features: profile.features || [],
        moduleSeats: profile.moduleSeats || [],
        tenantId: profile.tenantId || '',
        tenantName: profile.tenantName,
        organizationName: profile.organizationName,
        extensions: profile.extensions,
      };

      this._user.set(user);

      // Mirror features + seats into ui-core's shared services so the
      // plugin filter pipeline (slots, nav items, quick actions, stat
      // items) gates on the same source as our own hasFeature/hasModuleSeat.
      this.featureService.setFeatures(
        Object.fromEntries((user.features ?? []).map((f) => [f, true])),
      );
      this.seatService.setSeats(user.moduleSeats ?? []);
      this.permissionsService.useIdentity(user.id, user.tenantId);
      this.permissionsService.initFromLoginResponse(user.permissions, user.roles);
    } catch (error) {
      console.error('Failed to load user profile:', error);
      this._user.set(null);
      this.featureService.clear();
      this.seatService.clear();
      this.permissionsService.clear();
    }
  }

  /**
   * Clear user state (on logout)
   */
  clearUser(): void {
    this._user.set(null);
    this.authStore.logout();
    this.permissionsService.clear();
    this._isInitialized.set(false);
  }

  /**
   * Check if user has a specific permission
   */
  hasPermission(permission: string): boolean {
    return this.permissions().includes(permission);
  }

  /**
   * Check if user has any of the given permissions
   */
  hasAnyPermission(permissions: string[]): boolean {
    const userPermissions = this.permissions();
    return permissions.some((p) => userPermissions.includes(p));
  }

  /**
   * Check if user has all of the given permissions
   */
  hasAllPermissions(permissions: string[]): boolean {
    const userPermissions = this.permissions();
    return permissions.every((p) => userPermissions.includes(p));
  }

  /**
   * Check if user has a specific feature enabled
   */
  hasFeature(feature: string): boolean {
    return this.features().includes(feature);
  }

  /**
   * Check if the user holds a seat for the given module in this tenant.
   * Pair with hasFeature: feature = "tenant has the module licensed",
   * seat = "this user is allowed to use it".
   */
  hasModuleSeat(featureKey: string): boolean {
    return this.moduleSeats().some(
      (s) => s.localeCompare(featureKey, undefined, { sensitivity: 'accent' }) === 0,
    );
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
      `Silent renew failed (attempt ${this.silentRenewFailCount}/${WorkspaceAuthService.MAX_SILENT_RENEW_RETRIES}):`,
      error
    );

    if (this.silentRenewFailCount > WorkspaceAuthService.MAX_SILENT_RENEW_RETRIES) {
      this.redirectToLogin();
    }
  }

  /**
   * Redirect to login when session is unrecoverable.
   */
  private redirectToLogin(): void {
    console.warn('Session unrecoverable — redirecting to login');
    rememberReturnUrl();
    this.clearUser();
    this.oidcService.authorize();
  }
}
