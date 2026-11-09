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
