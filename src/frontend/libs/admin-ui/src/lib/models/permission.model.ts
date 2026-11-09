export interface Permission {
  name: string;
  displayName: string;
  description?: string;
  requiresSuperAdmin?: boolean;
}

export interface PermissionCategory {
  name: string;
  permissions: Permission[];
}

export interface PermissionsResponse {
  categories: PermissionCategory[];
}
