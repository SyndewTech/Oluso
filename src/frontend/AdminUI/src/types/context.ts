// Types for admin context and tenant/organization switching

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
