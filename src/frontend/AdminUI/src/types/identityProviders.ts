// Provider type is just a number from the API - no need to duplicate enum values
// The API's /types endpoint returns all metadata including names and descriptions
export type ExternalProviderType = number;

// Well-known provider type values (for reference/type-safety in switch statements)
// These values come from the backend C# enum but the frontend should use the API response
export const ProviderTypes = {
  Google: 0,
  Microsoft: 1,
  Facebook: 2,
  Apple: 3,
  GitHub: 4,
  LinkedIn: 5,
  Twitter: 6,
  Oidc: 7,
  OAuth2: 8,
  Saml2: 9,
  Ldap: 10,
} as const;

export interface IdentityProvider {
  id: number;
  scheme: string;
  displayName?: string;
  enabled: boolean;
  providerType: ExternalProviderType;
  iconUrl?: string;
  displayOrder: number;
  allowedClientIds: string[];
  configuration?: Record<string, unknown>;
  created: string;
  updated?: string;
  lastAccessed?: string;
}

/** DTO for editing - includes unmasked configuration */
export interface IdentityProviderEdit {
  id: number;
  scheme: string;
  displayName?: string;
  enabled: boolean;
  providerType: ExternalProviderType;
  iconUrl?: string;
  displayOrder: number;
  allowedClientIds: string[];
  /** Full configuration including secrets (unmasked) */
  configuration?: Record<string, unknown>;
  nonEditable: boolean;
  created: string;
  updated?: string;
  lastAccessed?: string;
}

export interface CreateIdentityProviderRequest {
  scheme: string;
  displayName?: string;
  enabled?: boolean;
  providerType: ExternalProviderType;
  iconUrl?: string;
  displayOrder?: number;
  allowedClientIds?: string[];
  configuration?: Record<string, unknown>;
}

export interface UpdateIdentityProviderRequest {
  displayName?: string;
  enabled?: boolean;
  /** Provider type - can only be changed if configuration is compatible */
  providerType?: ExternalProviderType;
  iconUrl?: string;
  displayOrder?: number;
  allowedClientIds?: string[];
  /** Provider-specific configuration. Masked secrets ("••••••••") will preserve original values. */
  configuration?: Record<string, unknown>;
}

export interface ProviderTypeInfo {
  type: ExternalProviderType;
  name: string;
  description: string;
  iconUrl?: string;
  requiredFields: string[];
  optionalFields: string[];
}

export interface TestResult {
  success: boolean;
  message: string;
}

// Base configuration with proxy mode settings (shared by all providers)
export interface BaseProviderConfiguration {
  clientId: string;
  clientSecret?: string;
  scopes?: string[];
  claimMappings?: Record<string, string>;
  getClaimsFromUserInfoEndpoint?: boolean;
  saveTokens?: boolean;
  callbackPath?: string;

  // Proxy mode settings
  proxyMode?: boolean;
  storeUserLocally?: boolean;
  cacheExternalTokens?: boolean;
  tokenCacheDurationSeconds?: number;
  enableUserInfoProxy?: boolean;
  proxyIncludeClaims?: string[];
  proxyExcludeClaims?: string[];
  includeExternalAccessToken?: boolean;
  includeExternalIdToken?: boolean;
}

// Configuration types for different providers
export interface OidcConfiguration extends BaseProviderConfiguration {
  authority: string;
  validIssuer?: string;
  responseType?: string;
  responseMode?: string;
  usePkce?: boolean;
  additionalAuthorizationParameters?: Record<string, string>;

  // OIDC endpoint overrides
  disableDiscovery?: boolean;
  authorizationEndpoint?: string;
  tokenEndpoint?: string;
  userInfoEndpoint?: string;
  jwksUri?: string;
  endSessionEndpoint?: string;
  revocationEndpoint?: string;
  introspectionEndpoint?: string;

  // Token validation options
  validateIssuer?: boolean;
  validateAudience?: boolean;
  validateLifetime?: boolean;
  clockSkewSeconds?: number;
  requireSignedTokens?: boolean;
  allowedSigningAlgorithms?: string[];
}

export interface GoogleConfiguration {
  clientId: string;
  clientSecret?: string;
  scopes?: string[];
  hostedDomain?: string;
  accessType?: string;
}

export interface MicrosoftConfiguration {
  clientId: string;
  clientSecret?: string;
  tenantId?: string;
  instance?: string;
  scopes?: string[];
  domainHint?: string;
  prompt?: string;
}

export interface GitHubConfiguration {
  clientId: string;
  clientSecret?: string;
  scopes?: string[];
  enterpriseUrl?: string;
  allowedOrganizations?: string[];
  requestEmail?: boolean;
}

export interface FacebookConfiguration {
  clientId: string;
  clientSecret?: string;
  scopes?: string[];
  fields?: string[];
}

export interface AppleConfiguration {
  clientId: string;
  teamId: string;
  keyId: string;
  privateKey: string;
  scopes?: string[];
}

export interface LinkedInConfiguration {
  clientId: string;
  clientSecret?: string;
  scopes?: string[];
}

export interface TwitterConfiguration {
  clientId: string;
  clientSecret?: string;
  scopes?: string[];
}

export interface OAuth2Configuration {
  clientId: string;
  clientSecret?: string;
  authorizationEndpoint: string;
  tokenEndpoint: string;
  userInfoEndpoint?: string;
  scopes?: string[];
  userIdClaimPath?: string;
  emailClaimPath?: string;
  nameClaimPath?: string;
}

export interface LdapConfiguration {
  // Connection settings
  server: string;
  port?: number;
  useSsl?: boolean;
  useStartTls?: boolean;
  validateCertificate?: boolean;

  // Bind credentials (service account)
  bindDn?: string;
  bindPassword?: string;

  // Search settings
  baseDn: string;
  userSearchBase?: string;
  userSearchFilter?: string;
  groupSearchBase?: string;
  groupSearchFilter?: string;

  // Attribute mappings
  uidAttribute?: string;
  emailAttribute?: string;
  displayNameAttribute?: string;
  firstNameAttribute?: string;
  lastNameAttribute?: string;
  phoneAttribute?: string;
  memberOfAttribute?: string;

  // Group to role mappings
  groupRoleMappings?: Record<string, string>;

  // Options
  autoProvisionUsers?: boolean;
  syncGroupsToRoles?: boolean;
  connectionTimeout?: number;
  searchTimeout?: number;
}

export interface Saml2Configuration {
  // Identity Provider settings
  entityId: string;
  metadataUrl?: string;
  singleSignOnServiceUrl?: string;
  singleSignOnBinding?: 'Redirect' | 'POST';
  singleLogoutServiceUrl?: string;
  singleLogoutBinding?: 'Redirect' | 'POST';

  // Certificates
  signingCertificate?: string;
  encryptionCertificate?: string;

  // Service Provider settings
  assertionConsumerServiceUrl?: string;
  signAuthenticationRequests?: boolean;
  wantAssertionsSigned?: boolean;
  wantResponsesSigned?: boolean;

  // Name ID
  nameIdFormat?: 'unspecified' | 'emailAddress' | 'persistent' | 'transient';

  // Claim mappings
  claimMappings?: Record<string, string>;

  // Options
  autoProvisionUsers?: boolean;
  allowedClockSkewSeconds?: number;
}
