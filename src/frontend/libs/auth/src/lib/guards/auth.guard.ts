import { inject } from '@angular/core';
import { Router, CanActivateFn, ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { AuthStore } from '../stores/auth.store';
import { PermissionsService } from '@syndew/oluso-ui-core';

/**
 * Auth Guard
 *
 * Protects routes that require authentication.
 * Redirects to login if user is not authenticated.
 */
export const authGuard: CanActivateFn = (
  route: ActivatedRouteSnapshot,
  state: RouterStateSnapshot
) => {
  const authStore = inject(AuthStore);
  const router = inject(Router);

  if (authStore.isAuthenticated()) {
    return true;
  }

  // Store the attempted URL for redirecting after login
  const returnUrl = state.url;

  // Redirect to the login page with the return url
  return router.createUrlTree(['/login'], {
    queryParams: { returnUrl },
  });
};

/**
 * Guest Guard
 *
 * Protects routes that should only be accessible to non-authenticated users.
 * Redirects to dashboard if user is already authenticated.
 */
export const guestGuard: CanActivateFn = () => {
  const authStore = inject(AuthStore);
  const router = inject(Router);

  if (!authStore.isAuthenticated()) {
    return true;
  }

  // Redirect to dashboard if already authenticated
  return router.createUrlTree(['/']);
};

/**
 * Super Admin Guard
 *
 * Protects routes that require super admin privileges.
 * Checks PermissionsService which is set during login/permission loading.
 */
export const superAdminGuard: CanActivateFn = () => {
  const permissionsService = inject(PermissionsService);
  const authStore = inject(AuthStore);
  const router = inject(Router);

  // Check PermissionsService first (set during login), fallback to user roles
  if (permissionsService.isSuperAdmin()) {
    return true;
  }

  // Fallback: check user roles directly
  const user = authStore.user();
  if (user?.roles?.some(r => r.toLowerCase() === 'superadmin' || r.toLowerCase() === 'systemadmin')) {
    return true;
  }

  // Redirect to dashboard if not super admin
  return router.createUrlTree(['/']);
};

/**
 * Org Admin Guard
 *
 * Protects routes that require org admin or super admin privileges.
 * Super admins can also access org admin routes.
 * Requires a valid organization context (currentOrganization) in the auth store.
 */
export const orgAdminGuard: CanActivateFn = () => {
  const permissionsService = inject(PermissionsService);
  const authStore = inject(AuthStore);
  const router = inject(Router);

  // Super admins can also access org admin routes
  if (permissionsService.isSuperAdmin()) return true;

  const user = authStore.user();
  if (user?.roles?.some(r =>
    r.toLowerCase() === 'superadmin' || r.toLowerCase() === 'systemadmin'
  )) return true;

  // Org admin: must have a current organization context set
  const currentOrg = authStore.currentOrganization();
  if (currentOrg?.id) return true;

  return router.createUrlTree(['/']);
};
