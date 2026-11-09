/**
 * Module Roles Component Types
 *
 * These types are used by the reusable ModuleRolesComponent to manage
 * delegated role administration within an individual module.
 */

/**
 * Configuration for a module's role management page
 */
export interface ModuleRolesConfig {
  /** Permission prefix for this module (the module's permission namespace) */
  modulePrefix: string;

  /** Display name for this module (shown in the page header) */
  moduleName: string;

  /** Wildcard permission for this module (e.g., '<prefix>.*') */
  wildcardPermission: string;

  /** Optional description for the info banner */
  infoBannerDescription?: string;

  /** Whether to show the delegated administration info banner */
  showInfoBanner?: boolean;
}

/**
 * Role data transfer object for module roles
 */
export interface ModuleRoleDto {
  id: string;
  name: string;
  displayName: string;
  description?: string;
  permissions: string[];
  managedByRole?: string;
  isBuiltIn: boolean;
  createdAt: string;
  updatedAt: string;
}

/**
 * User data transfer object for role assignment
 */
export interface ModuleRoleUserDto {
  id: string;
  email: string;
  displayName?: string;
}

/**
 * Request to create a new module role
 */
export interface CreateModuleRoleRequest {
  name: string;
  displayName: string;
  description?: string;
  permissions: string[];
}

/**
 * Response containing available permissions for a module
 */
export interface ModulePermissionsResponse {
  /** Permissions grouped by category */
  categories: { [category: string]: string[] };

  /** List of sensitive permissions that should be highlighted */
  sensitivePermissions: string[];
}
