import { HttpContextToken } from '@angular/common/http';

/**
 * Mark an HTTP request whose failures the caller handles itself, so the global
 * error interceptor does NOT pop a red toast for it. The request still rejects —
 * this only suppresses the toast, not the error.
 *
 * Use for "chained, not-required" calls that can legitimately 403/404 as part of
 * normal flow (e.g. a conversation thread that may not exist or the viewer can't
 * see), where a toast would be noise:
 *
 *   this.http.get(url, { context: new HttpContext().set(SKIP_ERROR_TOAST, true) })
 */
export const SKIP_ERROR_TOAST = new HttpContextToken<boolean>(() => false);
