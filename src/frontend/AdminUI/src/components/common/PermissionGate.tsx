import React from 'react';
import { usePermissions } from '../../hooks/usePermissions';

interface PermissionGateProps {
  /** Required permission to view children */
  permission?: string;
  /** Required permissions (user must have ALL) */
  permissions?: string[];
  /** User must have any of these permissions */
  anyPermission?: string[];
  /** Content to render when permission check passes */
  children: React.ReactNode;
  /** Optional fallback to render when permission check fails */
  fallback?: React.ReactNode;
  /** If true, show an access denied message instead of hiding */
  showAccessDenied?: boolean;
}

/**
 * Conditionally renders children based on user permissions.
 *
 * @example
 * ```tsx
 * // Single permission
 * <PermissionGate permission="users.read">
 *   <UsersPage />
 * </PermissionGate>
 *
 * // All permissions required
 * <PermissionGate permissions={['users.read', 'users.write']}>
 *   <UserEditForm />
 * </PermissionGate>
 *
 * // Any permission
 * <PermissionGate anyPermission={['users.write', 'users.delete']}>
 *   <UserActionsPanel />
 * </PermissionGate>
 *
 * // With access denied message
 * <PermissionGate permission="admin.settings" showAccessDenied>
 *   <AdminSettings />
 * </PermissionGate>
 * ```
 */
export function PermissionGate({
  permission,
  permissions,
  anyPermission,
  children,
  fallback,
  showAccessDenied = false,
}: PermissionGateProps) {
  const { hasPermission, hasAllPermissions, hasAnyPermission, isLoading, isLoaded } = usePermissions();

  // While loading, optionally show nothing or a loading state
  if (!isLoaded || isLoading) {
    return null;
  }

  let hasAccess = true;

  // Check single permission
  if (permission) {
    hasAccess = hasPermission(permission);
  }

  // Check all permissions (AND logic)
  if (hasAccess && permissions && permissions.length > 0) {
    hasAccess = hasAllPermissions(permissions);
  }

  // Check any permission (OR logic)
  if (hasAccess && anyPermission && anyPermission.length > 0) {
    hasAccess = hasAnyPermission(anyPermission);
  }

  if (hasAccess) {
    return <>{children}</>;
  }

  if (showAccessDenied) {
    return <AccessDenied />;
  }

  if (fallback) {
    return <>{fallback}</>;
  }

  return null;
}

function AccessDenied() {
  return (
    <div className="rounded-lg border border-red-200 bg-red-50 p-4">
      <div className="flex">
        <div className="flex-shrink-0">
          <svg className="h-5 w-5 text-red-400" viewBox="0 0 20 20" fill="currentColor">
            <path
              fillRule="evenodd"
              d="M5 9V7a5 5 0 0110 0v2a2 2 0 012 2v5a2 2 0 01-2 2H5a2 2 0 01-2-2v-5a2 2 0 012-2zm8-2v2H7V7a3 3 0 016 0z"
              clipRule="evenodd"
            />
          </svg>
        </div>
        <div className="ml-3">
          <h3 className="text-sm font-medium text-red-800">Access Denied</h3>
          <div className="mt-2 text-sm text-red-700">
            <p>You don't have permission to access this feature.</p>
            <p className="mt-1">Contact your administrator if you believe this is an error.</p>
          </div>
        </div>
      </div>
    </div>
  );
}

/**
 * Hook-based helper for conditionally rendering content based on permissions.
 * Use this when you need more control than PermissionGate provides.
 *
 * @example
 * ```tsx
 * function UserActions() {
 *   const canEdit = useHasPermission('users.write');
 *   const canDelete = useHasPermission('users.delete');
 *
 *   return (
 *     <div>
 *       {canEdit && <EditButton />}
 *       {canDelete && <DeleteButton />}
 *     </div>
 *   );
 * }
 * ```
 */
export function RequirePermission({
  permission,
  children,
  fallback = null,
}: {
  permission: string;
  children: React.ReactNode;
  fallback?: React.ReactNode;
}) {
  const { hasPermission, isLoaded } = usePermissions();

  if (!isLoaded) {
    return null;
  }

  return hasPermission(permission) ? <>{children}</> : <>{fallback}</>;
}

export default PermissionGate;
