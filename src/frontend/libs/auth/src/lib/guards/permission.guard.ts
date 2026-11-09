import { InjectionToken, inject } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { Router, CanActivateFn, ActivatedRouteSnapshot, UrlTree } from '@angular/router';
import { Observable, filter, map, of, take, timeout } from 'rxjs';
import { PermissionsService } from '@syndew/oluso-ui-core';

/**
 * How long a permission guard waits for the app to load the current user's
 * permissions before denying. Override to tune per app.
 */
export const PERMISSION_GUARD_TIMEOUT_MS = new InjectionToken<number>(
  'PERMISSION_GUARD_TIMEOUT_MS',
  { providedIn: 'root', factory: () => 15000 }
);

/**
 * Runs `check` once permissions are authoritative for the current user, and
 * denies (redirect to /unauthorized) if they never arrive, failed to load, or
 * were cleared. Must be called from a guard's injection context.
 */
function decideWhenPermissionsSettle(
  check: (permissions: PermissionsService) => boolean
): boolean | UrlTree | Observable<boolean | UrlTree> {
  const permissionsService = inject(PermissionsService);
  const router = inject(Router);
  const waitMs = inject(PERMISSION_GUARD_TIMEOUT_MS);

  const decide = (): boolean | UrlTree =>
    permissionsService.isAuthoritative() && check(permissionsService)
      ? true
      : router.createUrlTree(['/unauthorized']);

  if (permissionsService.isSettled()) {
    return decide();
  }

  return toObservable(permissionsService.isSettled).pipe(
    filter(Boolean),
    take(1),
    timeout({ first: waitMs, with: () => of(false) }),
    map(() => decide())
  );
}

/**
 * Permission Guard
 *
 * Protects routes that require specific permissions.
 * Configure the required permission in route data.
 *
 * Usage:
 * ```typescript
 * {
 *   path: 'users',
 *   component: UsersPageComponent,
 *   canActivate: [permissionGuard],
 *   data: { permission: 'users.read' }
 * }
 * ```
 */
export const permissionGuard: CanActivateFn = (route: ActivatedRouteSnapshot) => {
  const permission = route.data['permission'] as string | undefined;
  const permissions = route.data['permissions'] as string[] | undefined;
  const requireAll = route.data['requireAll'] as boolean | undefined;

  // No permission required
  if (!permission && !permissions) {
    return true;
  }

  return decideWhenPermissionsSettle((permissionsService) => {
    if (permission && permissionsService.hasPermission(permission)) {
      return true;
    }
    if (permissions) {
      return requireAll
        ? permissionsService.hasAllPermissions(permissions)
        : permissionsService.hasAnyPermission(permissions);
    }
    return false;
  });
};

/**
 * Any Permission Guard
 *
 * Allows access if user has any of the specified permissions.
 *
 * Usage:
 * ```typescript
 * {
 *   path: 'resources',
 *   component: ResourcesPageComponent,
 *   canActivate: [anyPermissionGuard],
 *   data: { permissions: ['resources.read', 'resources.write'] }
 * }
 * ```
 */
export const anyPermissionGuard: CanActivateFn = (route: ActivatedRouteSnapshot) => {
  const permissions = route.data['permissions'] as string[] | undefined;

  if (!permissions || permissions.length === 0) {
    return true;
  }

  return decideWhenPermissionsSettle((permissionsService) =>
    permissionsService.hasAnyPermission(permissions)
  );
};

/**
 * All Permissions Guard
 *
 * Allows access only if user has all of the specified permissions.
 *
 * Usage:
 * ```typescript
 * {
 *   path: 'admin',
 *   component: AdminPageComponent,
 *   canActivate: [allPermissionsGuard],
 *   data: { permissions: ['admin.read', 'admin.write'] }
 * }
 * ```
 */
export const allPermissionsGuard: CanActivateFn = (route: ActivatedRouteSnapshot) => {
  const permissions = route.data['permissions'] as string[] | undefined;

  if (!permissions || permissions.length === 0) {
    return true;
  }

  return decideWhenPermissionsSettle((permissionsService) =>
    permissionsService.hasAllPermissions(permissions)
  );
};
