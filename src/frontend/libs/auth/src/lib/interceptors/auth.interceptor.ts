import { HttpInterceptorFn, HttpRequest, HttpHandlerFn, HttpEvent, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, switchMap, throwError, ReplaySubject, take, timeout } from 'rxjs';
import { AuthStore } from '../stores/auth.store';
import { AuthService } from '../services/auth.service';

let isRefreshing = false;
let refreshResult$ = new ReplaySubject<string>(1);

/**
 * Auth Interceptor
 *
 * Adds the Authorization header with the access token to outgoing requests.
 * Handles 401 errors by attempting to refresh the token.
 */
export const authInterceptor: HttpInterceptorFn = (
  req: HttpRequest<unknown>,
  next: HttpHandlerFn
): Observable<HttpEvent<unknown>> => {
  const authStore = inject(AuthStore);
  const authService = inject(AuthService);
  const router = inject(Router);

  // Skip auth header for login/refresh endpoints
  if (isAuthEndpoint(req.url)) {
    return next(req);
  }

  const token = authStore.accessToken();

  if (token) {
    req = addAuthHeader(req, token);
  }

  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401 && token) {
        return handle401Error(req, next, authService, authStore, router);
      }
      return throwError(() => error);
    })
  );
};

/**
 * Check if the request is to an auth endpoint
 */
function isAuthEndpoint(url: string): boolean {
  const authEndpoints = ['/auth/login', '/auth/refresh', '/auth/logout'];
  return authEndpoints.some((endpoint) => url.includes(endpoint));
}

/**
 * Add Authorization header to request
 */
function addAuthHeader(req: HttpRequest<unknown>, token: string): HttpRequest<unknown> {
  return req.clone({
    setHeaders: {
      Authorization: `Bearer ${token}`,
    },
  });
}

/**
 * Handle 401 errors by refreshing the token.
 *
 * Uses a ReplaySubject so that:
 * - On success: all queued requests receive the new token and retry.
 * - On failure: all queued requests receive the error and stop waiting.
 * A timeout prevents indefinite hangs if the refresh endpoint is unresponsive.
 */
function handle401Error(
  req: HttpRequest<unknown>,
  next: HttpHandlerFn,
  authService: AuthService,
  authStore: AuthStore,
  router: Router
): Observable<HttpEvent<unknown>> {
  // If already logged out, don't attempt refresh — just fail the request
  if (!authStore.isAuthenticated()) {
    return throwError(() => new HttpErrorResponse({ status: 401 }));
  }

  if (!isRefreshing) {
    isRefreshing = true;
    refreshResult$ = new ReplaySubject<string>(1);

    return authService.refreshToken().pipe(
      timeout(15000),
      switchMap((response) => {
        isRefreshing = false;
        refreshResult$.next(response.accessToken);
        refreshResult$.complete();
        return next(addAuthHeader(req, response.accessToken));
      }),
      catchError((error) => {
        isRefreshing = false;
        refreshResult$.error(error);
        authStore.logout();
        router.navigate(['/login'], {
          queryParams: { returnUrl: router.url },
        });
        return throwError(() => error);
      })
    );
  }

  // Wait for the current refresh to complete (success or failure)
  return refreshResult$.pipe(
    take(1),
    switchMap((token) => next(addAuthHeader(req, token))),
    catchError(() => throwError(() => new HttpErrorResponse({ status: 401 })))
  );
}
