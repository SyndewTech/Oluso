export interface AuthUser {
  id: string;
  email: string;
  name?: string;
  userName?: string;
  displayName?: string;
  firstName?: string;
  lastName?: string;
  picture?: string;
  emailVerified?: boolean;
  roles?: string[];
  permissions?: string[];
  metadata?: Record<string, unknown>;
  organizationIds?: string[];
  organizationRoles?: Record<string, string>;
  isOrgAdmin?: boolean;
}
