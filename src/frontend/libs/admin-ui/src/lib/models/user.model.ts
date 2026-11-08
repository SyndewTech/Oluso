import { UserClaimDto } from './user-claim.model';

/** A role the user holds in the current tenant, as returned by the user-roles endpoint. */
export interface UserRoleDto {
  id: string;
  name: string;
  displayName?: string;
  isGlobal?: boolean;
}

export interface UserDto {
  id: string;
  email: string;
  userName?: string;
  firstName?: string;
  lastName?: string;
  name?: string;
  displayName?: string;
  picture?: string;
  profilePictureUrl?: string;
  phoneNumber?: string;
  emailVerified: boolean;
  phoneNumberVerified?: boolean;
  isActive: boolean;
  lockoutEnd?: string;
  lockoutEnabled?: boolean;
  accessFailedCount?: number;
  twoFactorEnabled: boolean;
  roles: string[];
  /** Tenant that owns this user record (their home tenant). */
  tenantId?: string;
  /** Owning tenant's name — present only when it isn't the current tenant. */
  ownerTenantName?: string;
  /**
   * The user reaches this tenant through a TenantMembership rather than living
   * in it. Profile, credentials and sessions are managed from their home tenant.
   */
  isExternalToTenant?: boolean;
  claims?: UserClaimDto[];
  /**
   * Arbitrary key-value bag written by journey step handlers
   * (e.g. SignUp claimMappings, TermsAcceptance acceptance versions).
   * Distinct from <c>claims</c>: claims are identity assertions issued
   * to tokens; customProperties are persisted user metadata that the
   * admin UI can edit.
   */
  customProperties?: Record<string, string>;
  metadata?: Record<string, unknown>;
  createdAt: string;
  updatedAt?: string;
  lastLoginAt?: string;
  /**
   * True when the user has at least one linked external identity provider
   * (e.g. Google, Azure AD, SAML). Orthogonal to <c>hasPassword</c> — a
   * mixed-mode user has both.
   */
  isFederated?: boolean;
  /**
   * Provider names of every linked external login. Empty when
   * <c>isFederated</c> is false.
   */
  linkedProviders?: string[];
  /**
   * True when the user has a local password set. Mixed-mode users
   * (created via password, later linked SSO) have <c>isFederated</c> AND
   * <c>hasPassword</c> both true — Reset Password should still be
   * available because they own a credential. Only pure SSO users
   * (federated AND <c>!hasPassword</c>) get the control hidden.
   */
  hasPassword?: boolean;
  /** On create with an invite: why the invite email could not be sent (the user was still created). */
  inviteError?: string | null;
}
