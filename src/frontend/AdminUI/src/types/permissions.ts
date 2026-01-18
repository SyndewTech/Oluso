export interface Permission {
  name: string;
  displayName: string;
  description: string;
  requiresSuperAdmin: boolean;
}

export interface PermissionCategory {
  name: string;
  permissions: Permission[];
}

export interface PermissionsResponse {
  categories: PermissionCategory[];
  totalCount: number;
}

export interface MyPermissionsResponse {
  effectivePermissions: string[];
  rolePermissions: Record<string, string[]>;
  isSuperAdmin: boolean;
}

export interface PermissionCheckResult {
  permission: string;
  hasPermission: boolean;
  grantedBy: string | null;
}
