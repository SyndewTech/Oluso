import type { TenantEnvironment } from './organization.model';
export interface Tenant {
  id: string;
  name: string;
  displayName?: string;
  description?: string;
  identifier: string;
  customDomain?: string;
  enabled: boolean;
  createdAt: string;
  updatedAt?: string;
  planId?: string;
  planExpiresAt?: string;
  allowSelfRegistration: boolean;
  requireTermsAcceptance: boolean;
  termsOfServiceUrl?: string;
  privacyPolicyUrl?: string;
  requireEmailVerification: boolean;
  allowedEmailDomains?: string;
  useJourneyFlow: boolean;
  /** With journey flow on: no named policy and none for the journey → standard pages (else an error). */
  journeyFallbackToStandard: boolean;
  hideEmbedBranding: boolean;
  defaultIdpScheme?: string;
  defaultSignInPolicyId?: string;
  defaultSsoUiMode?: 'Journey' | 'Standalone';
  allowPlatformOrgSignIn: boolean;
}

export interface PasswordPolicy {
  minimumLength: number;
  maximumLength: number;
  requireDigit: boolean;
  requireLowercase: boolean;
  requireUppercase: boolean;
  requireNonAlphanumeric: boolean;
  requiredUniqueChars: number;
  passwordHistoryCount: number;
  passwordExpirationDays: number;
  maxFailedAttempts: number;
  lockoutDurationMinutes: number;
  blockCommonPasswords: boolean;
  /** Reject passwords found in the Have I Been Pwned breach corpus (k-anonymity range lookup). */
  checkBreachedPasswords: boolean;
  /** When the breach service can't be reached: false accepts the password, true rejects it. */
  rejectWhenBreachCheckUnavailable: boolean;
  customRegexPattern?: string;
  customRegexErrorMessage?: string;
}

export interface CreateTenantRequest {
  name: string;
  displayName?: string;
  identifier: string;
  description?: string;
  customDomain?: string;
  environment?: TenantEnvironment;
}

export interface UpdateTenantRequest {
  name?: string;
  displayName?: string;
  description?: string;
  customDomain?: string;
  enabled?: boolean;
  hideEmbedBranding?: boolean;
  defaultIdpScheme?: string;
  defaultSignInPolicyId?: string;
  defaultSsoUiMode?: '' | 'Journey' | 'Standalone';
  allowPlatformOrgSignIn?: boolean;
}

export interface UpdatePasswordPolicyRequest {
  minimumLength?: number;
  maximumLength?: number;
  requireDigit?: boolean;
  requireLowercase?: boolean;
  requireUppercase?: boolean;
  requireNonAlphanumeric?: boolean;
  requiredUniqueChars?: number;
  passwordHistoryCount?: number;
  passwordExpirationDays?: number;
  maxFailedAttempts?: number;
  lockoutDurationMinutes?: number;
  blockCommonPasswords?: boolean;
  checkBreachedPasswords?: boolean;
  rejectWhenBreachCheckUnavailable?: boolean;
  customRegexPattern?: string;
  customRegexErrorMessage?: string;
}

export const DEFAULT_PASSWORD_POLICY: PasswordPolicy = {
  minimumLength: 8,
  maximumLength: 128,
  requireDigit: true,
  requireLowercase: true,
  requireUppercase: true,
  requireNonAlphanumeric: true,
  requiredUniqueChars: 4,
  passwordHistoryCount: 0,
  passwordExpirationDays: 0,
  maxFailedAttempts: 5,
  lockoutDurationMinutes: 15,
  blockCommonPasswords: true,
  checkBreachedPasswords: false,
  rejectWhenBreachCheckUnavailable: false,
};

export interface ProtocolConfiguration {
  allowedGrantTypes?: string[];
  allowedResponseTypes?: string[];
  allowedTokenEndpointAuthMethods?: string[];
  subjectTypesSupported?: string[];
  idTokenSigningAlgValuesSupported?: string[];
  codeChallengeMethodsSupported?: string[];
  dPoPSigningAlgValuesSupported?: string[];
  requirePushedAuthorizationRequests: boolean;
  requirePkce: boolean;
  allowPlainPkce: boolean;
  enforceOAuth21: boolean;
  requireDPoP: boolean;
  claimsParameterSupported: boolean;
  requestParameterSupported: boolean;
  requestUriParameterSupported: boolean;
  frontchannelLogoutSupported: boolean;
  backchannelLogoutSupported: boolean;
  enableDynamicClientRegistration: boolean;
  allowOpenDynamicRegistration: boolean;
  dynamicRegistrationAllowedScopes?: string[];
  dynamicRegistrationAllowedGrantTypes?: string[];
  dynamicRegistrationRequirePkce: boolean;
  dynamicRegistrationMaxRedirectUris: number;
  dynamicRegistrationRequiredScope?: string;
  dynamicRegistrationRequiredClaim?: string;
  dynamicRegistrationRequiredClaimValue?: string;
  prefixScopeSuffixMaxLength: number;
  created: string;
  updated?: string;
}

export interface UpdateProtocolConfigurationRequest {
  enforceOAuth21?: boolean;
  enableDynamicClientRegistration?: boolean;
  allowOpenDynamicRegistration?: boolean;
  dynamicRegistrationAllowedScopes?: string[];
  dynamicRegistrationAllowedGrantTypes?: string[];
  dynamicRegistrationRequirePkce?: boolean;
  dynamicRegistrationMaxRedirectUris?: number;
  dynamicRegistrationRequiredScope?: string;
  dynamicRegistrationRequiredClaim?: string;
  dynamicRegistrationRequiredClaimValue?: string;
  prefixScopeSuffixMaxLength?: number;
}

export const DEFAULT_PROTOCOL_CONFIGURATION: Partial<ProtocolConfiguration> = {
  enableDynamicClientRegistration: false,
  allowOpenDynamicRegistration: false,
  dynamicRegistrationRequirePkce: true,
  dynamicRegistrationMaxRedirectUris: 10,
  prefixScopeSuffixMaxLength: 256,
};

// Tenant Feature interfaces

export interface TenantFeaturesResponse {
  tenantId: string;
  hasFeaturesConfigured: boolean;
  features: TenantFeature[];
}

export interface TenantFeature {
  key: string;
  displayName: string;
  description: string;
  enabled: boolean;
  orgEnabled: boolean;
  grantedAt?: string;
  maxSeats?: number | null;
  orgMaxSeats?: number | null;
}

export interface SetTenantFeaturesRequest {
  features: TenantFeatureUpdate[];
}

export interface TenantFeatureUpdate {
  featureKey: string;
  enabled: boolean;
  maxSeats?: number | null;
}
