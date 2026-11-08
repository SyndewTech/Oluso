import { HttpInterceptorFn, HttpRequest, HttpHandlerFn, HttpEvent } from '@angular/common/http';
import { Observable } from 'rxjs';

/**
 * Ngrok Interceptor
 *
 * Adds the ngrok-skip-browser-warning header to skip the ngrok interstitial page.
 * This header is required when making API calls through ngrok tunnels.
 */
export const ngrokInterceptor: HttpInterceptorFn = (
  req: HttpRequest<unknown>,
  next: HttpHandlerFn
): Observable<HttpEvent<unknown>> => {
  // Add ngrok header to skip browser warning
  const modifiedReq = req.clone({
    setHeaders: {
      'ngrok-skip-browser-warning': 'true',
    },
  });

  return next(modifiedReq);
};
