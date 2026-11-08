export interface Organization {
  id: string;
  name: string;
  slug: string;
  description?: string;
  enabled: boolean;
  logoUrl?: string;
  websiteUrl?: string;
  maxMembers: number;
  maxTenants: number;
  tenantCount: number;
  created: string;
  updated?: string;
}

export interface CreateOrganizationRequest {
  name: string;
  slug: string;
  description?: string;
  logoUrl?: string;
  websiteUrl?: string;
  maxMembers?: number;
  maxTenants?: number;
  ownerUserId?: string;
}

export interface UpdateOrganizationRequest {
  name?: string;
  slug?: string;
  description?: string;
  logoUrl?: string;
  websiteUrl?: string;
  maxMembers?: number;
  maxTenants?: number;
  enabled?: boolean;
}

export interface OrganizationMember {
  id: string;
  organizationId: string;
  userId: string;
  userName?: string;
  email?: string;
  displayName?: string;
  role: string;
  allowedTenantIds?: string[];
  joinedAt: string;
}

export interface OrganizationUser {
  id: string;
  userName?: string;
  email?: string;
  displayName?: string;
  tenantId?: string;
  tenantName?: string;
}

export type OrganizationRole = 'Owner' | 'Admin' | 'Member';

export type InvitationStatus = 'Pending' | 'Accepted' | 'Declined' | 'Expired' | 'Revoked';

export interface OrganizationInvitation {
  id: string;
  organizationId: string;
  organizationName?: string;
  email: string;
  role: OrganizationRole;
  status: InvitationStatus;
  allowedTenantIds?: string[];
  invitedByUserId: string;
  createdAt: string;
  expiresAt: string;
  acceptedAt?: string;
}

export interface CreateOrganizationInvitationRequest {
  email: string;
  role?: OrganizationRole;
  /** Omit or leave empty to grant access to every tenant in the organization. */
  allowedTenantIds?: string[];
  expirationDays?: number;
  message?: string;
}

export type TenantEnvironment = 'Development' | 'QA' | 'Staging' | 'UAT' | 'Production';

export interface OrganizationTenant {
  id: string;
  name: string;
  displayName?: string;
  identifier: string;
  description?: string;
  environment?: TenantEnvironment;
  enabled: boolean;
  created: string;
}

export interface OrganizationFeaturesResponse {
  organizationId: string;
  hasFeaturesConfigured: boolean;
  features: OrganizationFeature[];
}

export interface OrganizationFeature {
  key: string;
  displayName: string;
  description: string;
  enabled: boolean;
  grantedAt?: string;
  expiresAt?: string;
  maxSeats?: number | null;
  source: 'manual' | 'subscription';
  sourceSubscriptionId?: string;
}

export interface ProvisionOrganizationRequest {
  name: string;
  slug: string;
  description?: string;
  logoUrl?: string;
  websiteUrl?: string;
  maxMembers?: number;
  maxTenants?: number;
  tenantName?: string;
  tenantDisplayName?: string;
  tenantIdentifier: string;
  tenantDescription?: string;
  tenantEnvironment?: TenantEnvironment;
  adminEmail: string;
  adminFirstName?: string;
  adminLastName?: string;
  baseUrl?: string;
}

export interface ProvisionOrganizationResponse {
  organizationId: string;
  organizationName: string;
  tenantId: string;
  tenantIdentifier: string;
  adminUserId: string;
  adminEmail: string;
  isNewUser: boolean;
  welcomeEmailSent: boolean;
}

export interface MemberTenantAccess {
  tenantId: string;
  tenantName: string;
  tenantIdentifier: string;
  environment: TenantEnvironment;
  hasAccess: boolean;
  isHomeTenant: boolean;
  roles: string[];
  joinedAt?: string;
}

export interface SetOrganizationFeaturesRequest {
  features: FeatureUpdate[];
}

export interface FeatureUpdate {
  featureKey: string;
  enabled: boolean;
  expiresAt?: string;
  maxSeats?: number | null;
}
