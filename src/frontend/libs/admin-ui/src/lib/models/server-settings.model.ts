export interface TokenSettings {
  defaultAccessTokenLifetime: number;
  defaultIdentityTokenLifetime: number;
  defaultRefreshTokenLifetime: number;
}

/** How long the links and codes in account emails stay valid for this tenant. */
export interface AccountEmailSettings {
  /** Password-reset and email-confirmation links (5 – 43200). */
  accountLinkLifetimeMinutes: number;
  /** MFA codes sent by email or SMS (1 – 60). */
  mfaCodeLifetimeMinutes: number;
  /** Default invitation lifetime, also used on resend (1 – 90). */
  invitationLifetimeDays: number;
}

export const DEFAULT_ACCOUNT_EMAIL_SETTINGS: AccountEmailSettings = {
  accountLinkLifetimeMinutes: 1440,
  mfaCodeLifetimeMinutes: 10,
  invitationLifetimeDays: 7,
};

export interface SecuritySettings {
  requireHttps: boolean;
  emitStaticClaims: boolean;
  enableBackchannelLogout: boolean;
}

export interface CorsSettings {
  allowAllOrigins: boolean;
  allowedOrigins: string[];
}

export interface TenantSettings {
  useJourneyFlow: boolean;
  /** With journey flow on: no named policy and none for the journey → standard pages (else an error). */
  journeyFallbackToStandard: boolean;
  allowSelfRegistration: boolean;
  requireTermsAcceptance: boolean;
  termsOfServiceUrl?: string;
  privacyPolicyUrl?: string;
  requireEmailVerification: boolean;
  allowedEmailDomains?: string;
  enableLocalLogin: boolean;
  enablePasskey: boolean;
}

export interface ProtocolSettings {
  enforceOAuth21: boolean;
  enableDynamicClientRegistration: boolean;
  allowOpenDynamicRegistration: boolean;
  dynamicRegistrationAllowedScopes?: string[];
  dynamicRegistrationAllowedGrantTypes?: string[];
  dynamicRegistrationRequirePkce: boolean;
  dynamicRegistrationMaxRedirectUris: number;
  dynamicRegistrationRequiredScope?: string;
  dynamicRegistrationRequiredClaim?: string;
  dynamicRegistrationRequiredClaimValue?: string;
  /** Longest suffix a prefix scope accepts when it doesn't set its own maximum. */
  prefixScopeSuffixMaxLength: number;
}

/** Built-in default for {@link ProtocolSettings.prefixScopeSuffixMaxLength} (ApiScopePrefixes.DefaultSuffixMaxLength). */
export const DEFAULT_PREFIX_SCOPE_SUFFIX_MAX_LENGTH = 256;
export const MIN_PREFIX_SCOPE_SUFFIX_MAX_LENGTH = 1;
export const MAX_PREFIX_SCOPE_SUFFIX_MAX_LENGTH = 2048;

/** Why a tenant default suffix length can't be saved, or null. The server checks the same range. */
export function prefixScopeSuffixMaxLengthError(value: number | string | null | undefined): string | null {
  const n = value === null || value === undefined || value === '' ? NaN : Number(value);
  return Number.isInteger(n) && n >= MIN_PREFIX_SCOPE_SUFFIX_MAX_LENGTH && n <= MAX_PREFIX_SCOPE_SUFFIX_MAX_LENGTH
    ? null
    : `Enter a whole number from ${MIN_PREFIX_SCOPE_SUFFIX_MAX_LENGTH} to ${MAX_PREFIX_SCOPE_SUFFIX_MAX_LENGTH}.`;
}

export const DEFAULT_PROTOCOL_SETTINGS: ProtocolSettings = {
  enforceOAuth21: false,
  enableDynamicClientRegistration: false,
  allowOpenDynamicRegistration: false,
  dynamicRegistrationRequirePkce: true,
  dynamicRegistrationMaxRedirectUris: 10,
  prefixScopeSuffixMaxLength: DEFAULT_PREFIX_SCOPE_SUFFIX_MAX_LENGTH,
};

export interface BrandingSettings {
  /** Primary brand colour (hex, e.g. #1A56FF). */
  primaryColor?: string;
  /** Secondary / accent colour. */
  secondaryColor?: string;
  /** Page background colour. */
  backgroundColor?: string;
  /** Absolute URL to the company logo. Set by file upload. */
  logoUrl?: string;
  /** Absolute URL to the favicon. */
  faviconUrl?: string;
  /** Company name displayed on login pages. */
  companyName?: string;
  /** Support email shown in footers and error pages. */
  supportEmail?: string;
  /** Privacy policy URL surfaced on registration. */
  privacyPolicyUrl?: string;
  /** Terms of service URL surfaced on registration. */
  termsOfServiceUrl?: string;
  /** Optional CSS injected into the auth pages. */
  customCss?: string;
  /** Custom footer text shown on auth pages. */
  footerText?: string;
}

export const DEFAULT_BRANDING_SETTINGS: BrandingSettings = {};

export interface ServerSettings {
  tokenSettings: TokenSettings;
  securitySettings: SecuritySettings;
  corsSettings: CorsSettings;
  tenantSettings?: TenantSettings;
  protocolSettings?: ProtocolSettings;
  brandingSettings?: BrandingSettings;
  accountEmailSettings?: AccountEmailSettings;
}
