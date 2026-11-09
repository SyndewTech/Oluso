import { Injectable, inject, signal, computed } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { checkAuthRenewingSession } from '@syndew/oluso-auth';
import { PluginService } from '@syndew/oluso-ui-core';
import { ApiService } from './api.service';

export interface AccountMe {
  id: string;
  email?: string;
  displayName: string;
  profilePictureUrl?: string;
  tenantId?: string;
  features: string[];
  /** Per-module facts about this user, keyed by module (e.g. { emr: { isPatient: true } }). */
  extensions?: Record<string, Record<string, unknown>>;
}

/**
 * Account authentication and authorization service.
 * Loads the user's account profile including tenant features.
 * Used by AccountPluginService to filter navigation by enabled features.
 */
@Injectable({
  providedIn: 'root',
})
export class AccountAuthService {
  private readonly api = inject(ApiService);
  private readonly oidcService = inject(OidcSecurityService);
  private readonly router = inject(Router);
  private readonly plugins = inject(PluginService);

  private readonly _me = signal<AccountMe | null>(null);
  private readonly _isLoading = signal(false);
  private readonly _isInitialized = signal(false);

  readonly me = this._me.asReadonly();
  readonly isLoading = this._isLoading.asReadonly();
  readonly isInitialized = this._isInitialized.asReadonly();
  readonly features = computed(() => this._me()?.features ?? []);

  /**
   * Initialize by checking OIDC auth state and loading the user's account /me data.
   * Must run during APP_INITIALIZER so the token is available for the /me call.
   */
  async initialize(): Promise<void> {
    if (this._isInitialized()) return;

    this._isLoading.set(true);
    try {
      // Ensure OIDC has validated the token before making authenticated API calls
      const authResult = await firstValueFrom(checkAuthRenewingSession(this.oidcService));
      if (!authResult.isAuthenticated) {
        // Not logged in — skip /me, plugins will show unfiltered
        return;
      }

      const me = await firstValueFrom(this.api.get<AccountMe>('/me'));
      this._me.set(me);
    } catch (error) {
      console.error('Failed to load account /me:', error);
      // Non-fatal — core account pages still work, plugins just won't filter.
      // Most common cause of a checkAuth() throw is a stale ?code=&state=
      // pair on the URL whose stored state was cleared (back button, fresh
      // tab). Clean the params and route to /profile; the guard handles
      // unauthenticated users.
      this._me.set(null);
      this.recoverFromStaleCallback();
    } finally {
      this._isLoading.set(false);
      this._isInitialized.set(true);
    }
  }

  hasFeature(feature: string): boolean {
    return this.features().includes(feature);
  }

  /** Where this user lands when they didn't ask for a page: the first plugin home they qualify for, else their profile. */
  homePath(): string {
    const home = this.plugins
      .accountPlugins()
      .map((plugin) => plugin.home)
      .find((h) => h && (!h.feature || this.hasFeature(h.feature)) && (!h.requires || this.hasExtensionFlag(h.requires)));
    return home?.path ?? '/profile';
  }

  /** True when the "{module}.{flag}" extension is exactly true for this user. */
  hasExtensionFlag(path: string): boolean {
    const dot = path.indexOf('.');
    if (dot <= 0) return false;
    const bag = this._me()?.extensions?.[path.slice(0, dot)];
    return bag?.[path.slice(dot + 1)] === true;
  }

  /**
   * Re-read /me after the user changes their own profile (name, picture) so
   * the layout's user block reflects it without a reload.
   */
  async refresh(): Promise<void> {
    try {
      const me = await firstValueFrom(this.api.get<AccountMe>('/me'));
      this._me.set(me);
    } catch (error) {
      console.error('Failed to refresh account /me:', error);
    }
  }

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
    this.router.navigate(['/profile']).catch(() => {
      // Defensive: route may not exist in a custom host — fall through.
    });
  }
}
