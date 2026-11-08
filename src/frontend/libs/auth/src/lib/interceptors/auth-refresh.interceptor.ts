import { HttpInterceptorFn, HttpRequest, HttpHandlerFn, HttpEvent, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Observable, throwError } from 'rxjs';
import { switchMap, take, catchError, shareReplay, finalize } from 'rxjs/operators';
import { AuthProvider, AuthRefreshResult } from '../auth/auth-provider';

const RETURN_URL_KEY = 'oluso.returnUrl.active';

let refreshInFlight: Observable<AuthRefreshResult> | null = null;

/**
 * Token attachment fallback + 401 retry with refresh deduplication. On refresh
 * failure, stashes the current route in sessionStorage under an opaque key so
 * the post-login callback can restore navigation without leaking the route to
 * URL/history/server logs.
 *
 * Skips the OIDC token endpoint to avoid infinite loops if /connect/token 401s.
 */
export const authRefreshInterceptor: HttpInterceptorFn = (
  req: HttpRequest<unknown>,
  next: HttpHandlerFn
): Observable<HttpEvent<unknown>> => {
  const authProvider = inject(AuthProvider);

  if (req.headers.has('Authorization')) {
    return next(req).pipe(catchError((err) => handle401(err, req, next, authProvider)));
  }

  return authProvider.getAccessToken().pipe(
    take(1),
    switchMap((token) => {
      const outReq = token ? addToken(req, token) : req;
      return next(outReq).pipe(catchError((err) => handle401(err, req, next, authProvider)));
    })
  );
};

function addToken(req: HttpRequest<unknown>, token: string): HttpRequest<unknown> {
  return req.clone({
    setHeaders: { Authorization: `Bearer ${token}` },
  });
}

function handle401(
  error: unknown,
  req: HttpRequest<unknown>,
  next: HttpHandlerFn,
  authProvider: AuthProvider
): Observable<HttpEvent<unknown>> {
  if (
    error instanceof HttpErrorResponse &&
    error.status === 401 &&
    !error.url?.includes('/connect/token')
  ) {
    return sharedRefresh(authProvider).pipe(
      switchMap((result) => {
        if (result?.isAuthenticated && result?.accessToken) {
          return next(addToken(req, result.accessToken));
        }
        console.warn('Token refresh returned no valid session — redirecting to login');
        redirectToLogin(authProvider);
        return throwError(() => error);
      }),
      catchError((refreshError) => {
        console.warn('Token refresh failed — redirecting to login', refreshError);
        redirectToLogin(authProvider);
        return throwError(() => error);
      })
    );
  }
  return throwError(() => error);
}

function sharedRefresh(authProvider: AuthProvider): Observable<AuthRefreshResult> {
  if (!refreshInFlight) {
    refreshInFlight = authProvider.forceRefreshSession().pipe(
      take(1),
      finalize(() => { refreshInFlight = null; }),
      shareReplay({ bufferSize: 1, refCount: false })
    );
  }
  return refreshInFlight;
}

function redirectToLogin(authProvider: AuthProvider): void {
  rememberReturnUrl();
  authProvider.authorize();
}

/**
 * Remembers the page the user is on before a sign-in redirect ends their session, so the callback page can
 * bring them back to it ({@link readAndClearReturnUrl}). The callback, renew and error pages, and the root,
 * are not remembered.
 */
export function rememberReturnUrl(): void {
  try {
    const currentUrl = window.location.pathname + window.location.search + window.location.hash;
    if (
      !currentUrl.startsWith('/callback') &&
      !currentUrl.startsWith('/silent-renew') &&
      !currentUrl.startsWith('/error') &&
      currentUrl !== '/'
    ) {
      sessionStorage.setItem(RETURN_URL_KEY, currentUrl);
    }
  } catch {
    /* sessionStorage unavailable */
  }
}

/**
 * Helper for callback pages to restore the pre-login route.
 */
export function readAndClearReturnUrl(): string | null {
  try {
    const url = sessionStorage.getItem(RETURN_URL_KEY);
    sessionStorage.removeItem(RETURN_URL_KEY);
    return url;
  } catch {
    return null;
  }
}
