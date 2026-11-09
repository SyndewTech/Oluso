/**
 * Types for admin context and tenant/organization switching
 */

export interface AdminContext {
  userId: string | null;
  email: string | null;
  isSuperAdmin: boolean;
  isOrgAdmin: boolean;
  currentTenant: TenantSummary | null;
  currentOrganization: OrganizationSummary | null;
  availableTenants: TenantSwitch[];
  organizations: OrganizationMembershipSummary[];
}

export interface TenantSummary {
  id: string;
  name: string | null;
  identifier: string | null;
}

export interface OrganizationSummary {
  id: string;
  name: string | null;
  role: string | null;
}

export interface OrganizationMembershipSummary {
  organizationId: string;
  organizationName: string | null;
  organizationSlug: string | null;
  role: string;
}

export interface TenantSwitch {
  id: string;
  name: string;
  displayName: string | null;
  identifier: string;
  environment: string;
  organizationId: string | null;
  userRole: string | null;
  enabled: boolean;
  /** The user's home tenant, where clearing the tenant context returns to. */
  isHome: boolean;
}

export interface OrganizationSwitch {
  id: string;
  name: string;
  slug: string;
  role: string;
  tenantCount: number;
  enabled: boolean;
}

export interface TenantDetail {
  id: string;
  name: string;
  displayName: string | null;
  identifier: string;
  description: string | null;
  environment: string;
  enabled: boolean;
  organizationId: string | null;
  organizationName: string | null;
  created: string;
  updated: string | null;
}

/**
 * Environment badge color mapping
 */
export type EnvironmentBadgeVariant = 'default' | 'success' | 'warning' | 'error' | 'info';

export const ENVIRONMENT_COLORS: Record<string, EnvironmentBadgeVariant> = {
  Production: 'success',
  Staging: 'warning',
  Development: 'info',
  Testing: 'default',
};

export function getEnvironmentColor(environment: string): EnvironmentBadgeVariant {
  return ENVIRONMENT_COLORS[environment] || 'default';
}
