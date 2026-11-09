import { Observable, of } from 'rxjs';
import { catchError, switchMap, take } from 'rxjs/operators';

/** The parts of the app's OIDC client the startup check needs; the library's security service fits as is. */
export interface StartupAuthClient<R extends { isAuthenticated: boolean }> {
  /** Validates the stored tokens (or completes a sign-in callback) without calling the server. */
  checkAuth(): Observable<R>;
  /** {@link checkAuth}, then, when that finds no valid session, a refresh with the stored refresh token. */
  checkAuthIncludingServer(): Observable<R>;
  getRefreshToken(): Observable<string | null | undefined>;
}

const CALLBACK_PARAMS = ['code', 'state', 'error'];

/**
 * The app's startup auth check. A stored access token that has expired (or is inside the renew window) makes the
 * plain check report "signed out", and the app would send the user through sign-in again although the refresh
 * token is good for days. When a refresh token is stored this renews with it instead, which also restarts the
 * library's background renew. A sign-in callback is completed by the plain check alone, and a refresh the server
 * refuses (refresh token expired or revoked) leaves the user signed out, as before.
 *
 * The refresh goes through the app's HttpClient, so the token-refresh interceptor still coordinates it with the
 * app's other tabs.
 */
export function checkAuthRenewingSession<R extends { isAuthenticated: boolean }>(
  client: StartupAuthClient<R>,
  url: string = typeof window !== 'undefined' ? window.location.href : ''
): Observable<R> {
  return client.checkAuth().pipe(
    switchMap((result) => {
      if (result.isAuthenticated || isCallback(url)) return of(result);
      return client.getRefreshToken().pipe(
        take(1),
        switchMap((refreshToken) =>
          refreshToken ? client.checkAuthIncludingServer().pipe(catchError(() => of(result))) : of(result)
        )
      );
    })
  );
}

function isCallback(url: string): boolean {
  if (!url) return false;
  const params = new URL(url, 'http://localhost').searchParams;
  return CALLBACK_PARAMS.some((p) => params.has(p));
}
