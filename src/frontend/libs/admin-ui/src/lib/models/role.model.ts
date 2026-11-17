export interface RoleDto {
  id: string;
  name: string;
  displayName?: string;
  description?: string;
  isDefault?: boolean;
  isStatic?: boolean;
  isSystemRole?: boolean;
  isGlobal?: boolean;
  /** Server-side verdict: whether the current caller may delete this role. */
  isDeletable?: boolean;
  permissions?: string[];
  /** The tenant's own application entitlements granted by this role. */
  entitlements?: string[];
  claims?: RoleClaimDto[];
  createdAt: string;
  updatedAt?: string;
}

export interface RoleClaimDto {
  type: string;
  value: string;
}

export interface CreateRoleRequest {
  name: string;
  displayName?: string;
  description?: string;
  permissions?: string[];
  entitlements?: string[];
  claims?: RoleClaimDto[];
}

export interface UserInRoleDto {
  id: string;
  userName: string;
  email: string;
  displayName?: string;
  isActive: boolean;
}

/** What an import does, or did, for each role in a roles export file. */
export interface RoleImportResult {
  canImport: boolean;
  error?: string | null;
  applied: boolean;
  roles: RoleImportItem[];
  /** Entitlements the file uses that this organization lacks; the import creates them. */
  entitlementsToCreate: string[];
}

export interface RoleImportItem {
  name: string;
  action: 'create' | 'update' | 'skip' | 'invalid';
  reason?: string | null;
  droppedPermissions: string[];
  droppedClaims: string[];
  droppedEntitlements: string[];
  error?: string | null;
}
