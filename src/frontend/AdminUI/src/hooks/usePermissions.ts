import { useEffect } from 'react';
import { usePermissionsStore } from '../store/slices/permissionsSlice';
import { useAuthStore } from '../store/slices/authSlice';

/**
 * Hook for checking user permissions.
 *
 * @example
 * ```tsx
 * const { hasPermission, hasAnyPermission, isSuperAdmin } = usePermissions();
 *
 * if (hasPermission('users.read')) {
 *   // User can view users
 * }
 *
 * if (hasAnyPermission(['users.write', 'users.delete'])) {
 *   // User can modify users
 * }
 * ```
 */
export function usePermissions() {
  const {
    effectivePermissions,
    isSuperAdmin,
    isLoading,
    isLoaded,
    error,
    hasPermission,
    hasAnyPermission,
    hasAllPermissions,
    fetchMyPermissions,
    reset,
  } = usePermissionsStore();

  const { isAuthenticated } = useAuthStore();

  // Fetch permissions when user is authenticated and permissions haven't been loaded
  useEffect(() => {
    if (isAuthenticated && !isLoaded && !isLoading) {
      fetchMyPermissions();
    }
    // Reset permissions when user logs out
    if (!isAuthenticated && isLoaded) {
      reset();
    }
  }, [isAuthenticated, isLoaded, isLoading, fetchMyPermissions, reset]);

  return {
    effectivePermissions: Array.from(effectivePermissions),
    isSuperAdmin,
    isLoading,
    isLoaded,
    error,
    hasPermission,
    hasAnyPermission,
    hasAllPermissions,
    refresh: fetchMyPermissions,
  };
}

/**
 * Hook for checking a specific permission.
 *
 * @example
 * ```tsx
 * const canReadUsers = useHasPermission('users.read');
 * ```
 */
export function useHasPermission(permission: string): boolean {
  const { hasPermission, isLoaded, isLoading } = usePermissions();

  // While loading, assume no permission (safe default)
  if (!isLoaded || isLoading) {
    return false;
  }

  return hasPermission(permission);
}

/**
 * Hook for checking if user has any of the specified permissions.
 *
 * @example
 * ```tsx
 * const canModifyUsers = useHasAnyPermission(['users.write', 'users.delete']);
 * ```
 */
export function useHasAnyPermission(permissions: string[]): boolean {
  const { hasAnyPermission, isLoaded, isLoading } = usePermissions();

  if (!isLoaded || isLoading) {
    return false;
  }

  return hasAnyPermission(permissions);
}

/**
 * Hook for checking if user has all of the specified permissions.
 *
 * @example
 * ```tsx
 * const canManageUsers = useHasAllPermissions(['users.read', 'users.write', 'users.delete']);
 * ```
 */
export function useHasAllPermissions(permissions: string[]): boolean {
  const { hasAllPermissions, isLoaded, isLoading } = usePermissions();

  if (!isLoaded || isLoading) {
    return false;
  }

  return hasAllPermissions(permissions);
}
