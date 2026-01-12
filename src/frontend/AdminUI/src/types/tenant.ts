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
  customRegexPattern?: string;
  customRegexErrorMessage?: string;
}

export interface CreateTenantRequest {
  name: string;
  displayName?: string;
  identifier: string;
  description?: string;
  customDomain?: string;
}

export interface UpdateTenantRequest {
  name?: string;
  displayName?: string;
  description?: string;
  customDomain?: string;
  enabled?: boolean;
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
  requireDPoP: boolean;
  claimsParameterSupported: boolean;
  requestParameterSupported: boolean;
  requestUriParameterSupported: boolean;
  frontchannelLogoutSupported: boolean;
  backchannelLogoutSupported: boolean;
  // Dynamic Client Registration (RFC 7591)
  enableDynamicClientRegistration: boolean;
  allowOpenDynamicRegistration: boolean;
  dynamicRegistrationAllowedScopes?: string[];
  dynamicRegistrationAllowedGrantTypes?: string[];
  dynamicRegistrationRequirePkce: boolean;
  dynamicRegistrationMaxRedirectUris: number;
  created: string;
  updated?: string;
}

export interface UpdateProtocolConfigurationRequest {
  enableDynamicClientRegistration?: boolean;
  allowOpenDynamicRegistration?: boolean;
  dynamicRegistrationAllowedScopes?: string[];
  dynamicRegistrationAllowedGrantTypes?: string[];
  dynamicRegistrationRequirePkce?: boolean;
  dynamicRegistrationMaxRedirectUris?: number;
}

export const DEFAULT_PROTOCOL_CONFIGURATION: Partial<ProtocolConfiguration> = {
  enableDynamicClientRegistration: false,
  allowOpenDynamicRegistration: false,
  dynamicRegistrationRequirePkce: true,
  dynamicRegistrationMaxRedirectUris: 10,
};
