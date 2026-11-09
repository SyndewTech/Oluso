import { Observable, defer, of, Subject, throwError } from 'rxjs';
import { catchError, finalize, map, switchMap, take, timeout } from 'rxjs/operators';
import { AuthProvider, AuthRefreshResult } from './auth-provider';
import { requestTenantSwitch } from '../interceptors/token-refresh-retry.interceptor';

/**
 * Serialises token refreshes against the auth library's own background renew.
 *
 * With silent renew on, the library refreshes on a timer. If an API call fails
 * with 401 while that renew is still in flight, a second refresh request is
 * declined by the library, which reports "no session" even though the renew is
 * about to succeed. Left alone, the interceptor would treat that as a signed-out
 * user and redirect to login. Instead, wait for the running renew to settle and
 * answer with the token it produced.
 *
 * Library-agnostic: the adapter supplies the "renew started" and "renew settled"
 * streams and the single-shot refresh call.
 */
export abstract class RenewAwareAuthProvider extends AuthProvider {
  /** How long to wait for a running renew before giving up; matches the library's own timeout. */
  protected renewWaitMs = 20_000;

  private renewInFlight = false;
  private readonly settled$ = new Subject<boolean>();

  /** One refresh attempt through the library. Reports unauthenticated when the library declines. */
  protected abstract refreshOnce(): Observable<AuthRefreshResult>;

  /**
   * Adapters call this once, in their constructor, with the library's events:
   * `started$` whenever a background renew begins, `settled$` with true/false
   * whenever a renew (or any authentication result) completes.
   */
  protected trackRenew(started$: Observable<unknown>, settled$: Observable<boolean>): void {
    started$.subscribe(() => {
      this.renewInFlight = true;
    });
    settled$.subscribe((ok) => {
      this.renewInFlight = false;
      this.settled$.next(ok);
    });
  }

  forceRefreshSession(): Observable<AuthRefreshResult> {
    if (this.renewInFlight) {
      return this.awaitRenew();
    }
    return this.refreshOnce().pipe(
      switchMap((result) => {
        if (result.isAuthenticated && result.accessToken) {
          return of(result);
        }
        // The library declines a refresh while its own renew is running; if one
        // started since we checked, its outcome is the answer, not this refusal.
        return this.renewInFlight ? this.awaitRenew() : of(result);
      })
    );
  }

  /**
   * A refresh that switches tenant: {@link requestTenantSwitch} adds the token endpoint's `tenant` parameter to
   * that one refresh request, outside the library, so the library neither keeps it for later refreshes nor sees
   * a refusal (which it would answer by signing the user out). A renew already running is let finish first, so
   * the switch redeems the refresh token it leaves behind. Errors unless the new token is for `tenantId`; the
   * session is left where it was.
   */
  switchTenant(tenantId: string): Observable<AuthRefreshResult> {
    const ready: Observable<unknown> = this.renewInFlight ? this.awaitRenew() : of(null);
    return ready.pipe(
      switchMap(() =>
        defer(() => {
          requestTenantSwitch(tenantId);
          return this.refreshOnce();
        }).pipe(finalize(() => requestTenantSwitch(null)))
      ),
      switchMap((result) =>
        result.isAuthenticated && result.accessToken && tenantOfToken(result.accessToken) === tenantId
          ? of(result)
          : throwError(() => new Error(`The session was not switched to tenant ${tenantId}`))
      )
    );
  }

  private awaitRenew(): Observable<AuthRefreshResult> {
    return this.settled$.pipe(
      take(1),
      timeout(this.renewWaitMs),
      catchError(() => of(false)),
      switchMap((ok) =>
        ok
          ? this.getAccessToken().pipe(
              take(1),
              map((token) => ({ isAuthenticated: !!token, accessToken: token ?? null }))
            )
          : of({ isAuthenticated: false, accessToken: null })
      )
    );
  }
}

/** The tenant an access token is for (its `tenant_id` claim), or null when it names none or is not a JWT. */
export function tenantOfToken(accessToken: string): string | null {
  const part = accessToken.split('.')[1];
  if (!part) return null;
  try {
    const json = atob(part.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(part.length / 4) * 4, '='));
    const tenant = (JSON.parse(json) as { tenant_id?: unknown }).tenant_id;
    return typeof tenant === 'string' ? tenant : null;
  } catch {
    return null;
  }
}
