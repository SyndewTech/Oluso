import { Observable, of } from 'rxjs';
import { Injectable } from '@angular/core';

export interface AuthRefreshResult {
  isAuthenticated: boolean;
  accessToken: string | null;
}

/**
 * Implemented by the host app to bridge auth concerns to whatever auth library
 * (angular-auth-oidc-client, MSAL, etc.) is in use. Keeps libs/auth free of
 * library-specific dependencies — swapping providers is a single-file change.
 */
export abstract class AuthProvider {
  abstract readonly isAuthenticated$: Observable<boolean>;
  abstract getAccessToken(): Observable<string | null>;
  abstract forceRefreshSession(): Observable<AuthRefreshResult>;
  /**
   * Refreshes the session into another tenant: the library's new tokens (access and refresh) are for
   * `tenantId`, and later refreshes stay there. Errors when the server refuses the tenant (the user has no
   * access to it); the session stays signed in, where it was.
   */
  abstract switchTenant(tenantId: string): Observable<AuthRefreshResult>;
  abstract authorize(): void;
}

@Injectable({ providedIn: 'root' })
export class NoOpAuthProvider extends AuthProvider {
  readonly isAuthenticated$ = of(false);
  getAccessToken(): Observable<string | null> { return of(null); }
  forceRefreshSession(): Observable<AuthRefreshResult> {
    return of({ isAuthenticated: false, accessToken: null });
  }
  switchTenant(): Observable<AuthRefreshResult> {
    return of({ isAuthenticated: false, accessToken: null });
  }
  authorize(): void { /* no-op */ }
}
