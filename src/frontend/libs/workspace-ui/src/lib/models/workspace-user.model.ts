/**
 * Workspace user model
 */
export interface WorkspaceUser {
  id: string;
  userName: string;
  email: string;
  displayName: string;
  firstName?: string;
  lastName?: string;
  profilePictureUrl?: string;
  jobTitle?: string;
  department?: string;
  isManager: boolean;
  roles: string[];
  permissions: string[];
  features?: string[];
  /**
   * Per-user module seats granted in this tenant. Drives the app
   * switcher's `requiresSeat` filter — without a seat, the module's app
   * is hidden even if the tenant has the feature licensed.
   */
  moduleSeats?: string[];
  tenantId: string;
  tenantName?: string;
  organizationName?: string;
  extensions?: Record<string, unknown>;
}

/**
 * User profile response from /api/workspace/me
 * Contains identity/auth data (not module-specific).
 * Plugin-specific data is available in the extensions dictionary.
 */
export interface WorkspaceUserProfile {
  id: string;
  userName: string;
  email?: string;
  firstName?: string;
  lastName?: string;
  displayName: string;
  profilePictureUrl?: string;
  tenantId: string;
  tenantName?: string;
  organizationName?: string;
  roles: string[];
  permissions: string[];
  features?: string[];
  /** See {@link WorkspaceUser.moduleSeats}. */
  moduleSeats?: string[];
  extensions?: Record<string, unknown>;
}
