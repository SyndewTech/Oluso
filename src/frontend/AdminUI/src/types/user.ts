export interface User {
  id: string;
  userName: string;
  email: string;
  emailConfirmed: boolean;
  phoneNumber?: string;
  phoneNumberConfirmed: boolean;
  firstName?: string;
  lastName?: string;
  displayName?: string;
  profilePictureUrl?: string;
  isActive: boolean;
  twoFactorEnabled: boolean;
  lockoutEnabled: boolean;
  lockoutEnd?: string;
  accessFailedCount?: number;
  roles: string[];
  claims?: UserClaim[];
  externalId?: string;
  externalProvider?: string;
  createdAt: string;
  updatedAt?: string;
  lastLoginAt?: string;
}

export interface UserClaim {
  type: string;
  value: string;
}

export interface ExternalLogin {
  loginProvider: string;
  providerKey: string;
  providerDisplayName: string;
}

export interface UserSession {
  sessionId: string;
  clientId?: string;
  clientName?: string;
  ipAddress?: string;
  userAgent?: string;
  created: string;
  renewed: string;
  expires?: string;
  isCurrent: boolean;
}

export interface Role {
  id: string;
  name: string;
  displayName: string;
  description?: string;
  isSystemRole: boolean;
  isGlobal: boolean;
  tenantId?: string;
  permissions: string[];
  claims?: RoleClaim[];
  createdAt: string;
  updatedAt?: string;
}

export interface RoleClaim {
  type: string;
  value: string;
}

export interface CreateUserRequest {
  email: string;
  userName?: string;
  password?: string;
  firstName?: string;
  lastName?: string;
  displayName?: string;
  phoneNumber?: string;
  isActive?: boolean;
  emailConfirmed?: boolean;
  roles?: string[];
}

export interface UpdateUserRequest {
  firstName?: string;
  lastName?: string;
  displayName?: string;
  phoneNumber?: string;
  isActive?: boolean;
  emailConfirmed?: boolean;
  lockoutEnabled?: boolean;
}

export interface CreateRoleRequest {
  name: string;
  displayName?: string;
  description?: string;
  permissions?: string[];
  claims?: RoleClaim[];
}

export interface UpdateRoleRequest {
  name?: string;
  displayName?: string;
  description?: string;
  permissions?: string[];
  claims?: RoleClaim[];
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}
