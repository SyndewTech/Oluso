import { HttpInterceptorFn, HttpRequest, HttpHandlerFn, HttpEvent, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';
import { ToastService, SKIP_ERROR_TOAST } from '@syndew/oluso-ui-core';

/**
 * Error Interceptor
 *
 * Catches HTTP error responses and displays user-friendly toast notifications.
 * Extracts error messages from backend responses when available.
 * Skips 401 errors (handled by the auth interceptor).
 */
export const errorInterceptor: HttpInterceptorFn = (
  req: HttpRequest<unknown>,
  next: HttpHandlerFn
): Observable<HttpEvent<unknown>> => {
  const toast = inject(ToastService);

  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      // Skip 401s — the auth interceptor handles token refresh / logout
      if (error.status === 401) {
        return throwError(() => error);
      }

      // Caller opted out of the global toast — it handles this failure itself
      // (e.g. an optional conversation thread that may 403/404). Still rethrow.
      if (req.context.get(SKIP_ERROR_TOAST)) {
        return throwError(() => error);
      }

      const message = extractMessage(error);
      toast.error(message);

      return throwError(() => error);
    })
  );
};

/**
 * Extract a human-readable message from the error response.
 * Module controllers return { message: "..." }, the admin API returns
 * { error: "..." }, and unhandled failures arrive as ProblemDetails.
 */
function extractMessage(error: HttpErrorResponse): string {
  // Try to get the message from the response body
  const body = error.error;

  if (body) {
    // Backend returns { message: "...", requiredPermission?: "..." }
    if (typeof body === 'object' && body.message) {
      if (body.requiredPermission) {
        return `${body.message} (${body.requiredPermission})`;
      }
      return body.message;
    }
    // Backend returns { Message: "..." } (PascalCase)
    if (typeof body === 'object' && body.Message) {
      return body.Message;
    }
    // Admin API validation / business errors: { error: "..." }
    if (typeof body === 'object' && typeof body.error === 'string' && body.error) {
      return body.error;
    }
    // ProblemDetails: { title, detail }
    if (typeof body === 'object' && typeof body.detail === 'string' && body.detail) {
      return body.detail;
    }
    if (typeof body === 'object' && typeof body.title === 'string' && body.title) {
      return body.title;
    }
    // ASP.NET validation errors: { errors: { field: ["msg"] } }
    if (typeof body === 'object' && body.errors) {
      const firstField = Object.keys(body.errors)[0];
      if (firstField) {
        const msgs = body.errors[firstField];
        if (Array.isArray(msgs) && msgs.length > 0) {
          return msgs[0];
        }
      }
    }
    // Plain string body
    if (typeof body === 'string' && body.length < 200) {
      return body;
    }
  }

  // Fallback based on status code
  switch (error.status) {
    // No readable response: the connection dropped, or the browser blocked the response (e.g. CORS on a
    // cross-origin redirect). Not a server outage; those come back as 502/503/504 below.
    case 0:
      return 'The request did not complete. It was blocked by the browser or the connection was lost.';
    case 400:
      return 'Invalid request. Please check your input.';
    case 403:
      return 'You do not have permission to perform this action.';
    case 404:
      return 'The requested resource was not found.';
    case 409:
      return 'A conflict occurred. The resource may have been modified.';
    case 422:
      return 'The request could not be processed. Please check your input.';
    case 500:
      return 'An internal server error occurred. Please try again later.';
    case 502:
    case 503:
    case 504:
      return 'Unable to reach the server right now. Please try again shortly.';
    default:
      return `An error occurred (${error.status}). Please try again.`;
  }
}
