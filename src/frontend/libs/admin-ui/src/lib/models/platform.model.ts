/** One custom domain record across all organizations. */
export interface PlatformDomain {
  id: string;
  domain: string;
  status: 'Pending' | 'Verified' | 'Active' | 'Failed' | 'Removed' | string;
  tenantId: string;
  tenantName?: string | null;
  tenantIdentifier?: string | null;
  /** The organization's custom domain is this one, so it serves its sign-in pages. */
  isServing: boolean;
  externalProvisioningId?: string | null;
  createdAt: string;
  verifiedAt?: string | null;
  activatedAt?: string | null;
  lastCheckedAt?: string | null;
  lastRevalidatedAt?: string | null;
  gracePeriodStartedAt?: string | null;
  checkAttempts: number;
  failureReason?: string | null;
}

/** A platform-wide role (no owning organization). */
export interface PlatformRole {
  id: string;
  name: string;
  displayName: string;
  description?: string | null;
  isSystemRole: boolean;
  /** SuperAdmin and SystemAdmin apply in every organization. */
  appliesEverywhere: boolean;
  /** Follows organization membership, so it isn't assigned by hand. */
  isDerived: boolean;
  holderCount: number;
  assignmentCount: number;
}

/** One assignment of a platform role. */
export interface PlatformRoleHolder {
  userId: string;
  userName: string;
  email: string;
  displayName?: string | null;
  isActive: boolean;
  homeTenantId?: string | null;
  homeTenantName?: string | null;
  tenantId: string;
  tenantName?: string | null;
  assignedAt: string;
  assignedBy?: string | null;
  assignedByName?: string | null;
}

export interface PlatformRoleTenantOption {
  id: string;
  name: string;
  identifier?: string | null;
}

export interface PlatformRoleCandidate {
  id: string;
  email: string;
  displayName?: string | null;
  userName: string;
}
