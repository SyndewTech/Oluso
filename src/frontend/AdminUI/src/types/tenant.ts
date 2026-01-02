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
