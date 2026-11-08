import { HttpInterceptorFn, HttpRequest, HttpHandlerFn, HttpEvent } from '@angular/common/http';
import { inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AuthStore } from '../stores/auth.store';

/**
 * Paths that must never carry the X-Tenant-ID header.
 * OIDC endpoints use the global issuer; sending a tenant header
 * can change the resolved issuer/discovery document and break
 * token validation after a tenant switch.
 */
const TENANT_HEADER_EXCLUDED_PATHS = [
  '/connect/',
  '/.well-known/',
  '/discovery/',
  // Action-token links carry their own tenant; a signed-in session's tenant
  // must not leak into a customer-facing request.
  '/api/public/',
];

/**
 * Tenant Interceptor
 *
 * Adds the X-Tenant-ID header to outgoing API requests when the user
 * has selected a tenant context.
 *
 * This is used in the Admin UI to allow super admins to manage
 * resources across different tenants. The backend validates
 * whether the user is authorized to access the specified tenant.
 *
 * OIDC protocol endpoints are excluded — they must always use the
 * global (non-tenant) context so the issuer stays consistent.
 */
export const tenantInterceptor: HttpInterceptorFn = (
  req: HttpRequest<unknown>,
  next: HttpHandlerFn
): Observable<HttpEvent<unknown>> => {
  const authStore = inject(AuthStore);

  // Only a tenant picked in the UI goes out; otherwise the API takes the tenant from the token.
  const tenantId = authStore.tenantSwitched() ? authStore.currentTenantId() : null;

  // An explicitly-set header wins. The tenant-switch call has to target the
  // tenant being switched TO while the store still holds the current one —
  // setHeaders would otherwise overwrite it and switch you back to yourself.
  if (tenantId && !req.headers.has('X-Tenant-ID')) {
    // Don't attach tenant header to OIDC protocol endpoints
    const urlPath = new URL(req.url, window.location.origin).pathname;
    const isExcluded = TENANT_HEADER_EXCLUDED_PATHS.some(p =>
      urlPath.includes(p)
    );

    if (!isExcluded) {
      req = req.clone({
        setHeaders: {
          'X-Tenant-ID': tenantId,
        },
      });
    }
  }

  return next(req);
};

/**
 * Organization Interceptor
 *
 * Adds the X-Organization-ID header to outgoing requests when the user
 * has selected an organization context.
 *
 * This is used for multi-tenant applications where users can belong
 * to multiple organizations.
 */
export const organizationInterceptor: HttpInterceptorFn = (
  req: HttpRequest<unknown>,
  next: HttpHandlerFn
): Observable<HttpEvent<unknown>> => {
  const authStore = inject(AuthStore);

  const organization = authStore.currentOrganization();

  if (organization) {
    req = req.clone({
      setHeaders: {
        'X-Organization-ID': organization.id,
      },
    });
  }

  return next(req);
};
