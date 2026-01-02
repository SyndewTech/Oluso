export interface AllowedUser {
  subjectId: string;
  displayName?: string;
}

export interface ClientClaim {
  type: string;
  value: string;
}

export interface ClientProperty {
  key: string;
  value: string;
}

export enum TokenUsage {
  ReUse = 0,
  OneTimeOnly = 1,
}

export enum TokenExpiration {
  Sliding = 0,
  Absolute = 1,
}

export enum AccessTokenType {
  Jwt = 0,
  Reference = 1,
}

export interface Client {
  // Basic settings
  clientId: string;
  clientName?: string;
  description?: string;
  clientUri?: string;
  logoUri?: string;
  enabled: boolean;
  created: string;
  updated?: string;
  lastAccessed?: string;

  // Authentication settings
  requireClientSecret: boolean;
  requirePkce: boolean;
  allowPlainTextPkce: boolean;
  requireRequestObject: boolean;
  requireDPoP: boolean;
  requirePushedAuthorization: boolean;
  pushedAuthorizationLifetime: number;

  // Consent settings
  requireConsent: boolean;
  allowRememberConsent: boolean;
  consentLifetime?: number;

  // Token settings
  allowOfflineAccess: boolean;
  allowAccessTokensViaBrowser: boolean;
  alwaysIncludeUserClaimsInIdToken: boolean;
  accessTokenLifetime: number;
  identityTokenLifetime: number;
  authorizationCodeLifetime: number;
  absoluteRefreshTokenLifetime: number;
  slidingRefreshTokenLifetime: number;
  refreshTokenUsage: TokenUsage;
  refreshTokenExpiration: TokenExpiration;
  updateAccessTokenClaimsOnRefresh: boolean;
  accessTokenType: AccessTokenType;
  allowedIdentityTokenSigningAlgorithms?: string;
  includeJwtId: boolean;

  // Client claims settings
  alwaysSendClientClaims: boolean;
  clientClaimsPrefix: string;
  pairWiseSubjectSalt?: string;

  // Logout settings
  frontChannelLogoutUri?: string;
  frontChannelLogoutSessionRequired: boolean;
  backChannelLogoutUri?: string;
  backChannelLogoutSessionRequired: boolean;

  // SSO and device settings
  enableLocalLogin: boolean;
  userSsoLifetime?: number;
  userCodeType?: string;
  deviceCodeLifetime: number;

  // CIBA (Client Initiated Backchannel Authentication) settings
  cibaEnabled: boolean;
  cibaTokenDeliveryMode: string;
  cibaClientNotificationEndpoint?: string;
  cibaRequestLifetime: number;
  cibaPollingInterval: number;
  cibaRequireUserCode: boolean;

  // UI Flow settings
  // null = inherit from tenant, true = force journey, false = force standalone
  // Note: Cannot override tenant setting if tenant disables journey flow
  useJourneyFlow?: boolean | null;

  // Collections
  allowedGrantTypes: string[];
  redirectUris: string[];
  postLogoutRedirectUris: string[];
  allowedScopes: string[];
  allowedCorsOrigins: string[];
  claims: ClientClaim[];
  properties: ClientProperty[];
  identityProviderRestrictions: string[];
  allowedRoles: string[];
  allowedUsers: AllowedUser[];
}

export interface CreateClientRequest {
  // Required
  clientId: string;

  // Basic settings
  clientName?: string;
  description?: string;
  clientUri?: string;
  logoUri?: string;
  clientSecret?: string;
  enabled?: boolean;

  // Authentication settings
  requireClientSecret?: boolean;
  requirePkce?: boolean;
  allowPlainTextPkce?: boolean;
  requireRequestObject?: boolean;
  requireDPoP?: boolean;
  requirePushedAuthorization?: boolean;
  pushedAuthorizationLifetime?: number;

  // Consent settings
  requireConsent?: boolean;
  allowRememberConsent?: boolean;
  consentLifetime?: number;

  // Token settings
  allowOfflineAccess?: boolean;
  allowAccessTokensViaBrowser?: boolean;
  alwaysIncludeUserClaimsInIdToken?: boolean;
  accessTokenLifetime?: number;
  identityTokenLifetime?: number;
  authorizationCodeLifetime?: number;
  absoluteRefreshTokenLifetime?: number;
  slidingRefreshTokenLifetime?: number;
  refreshTokenUsage?: TokenUsage;
  refreshTokenExpiration?: TokenExpiration;
  updateAccessTokenClaimsOnRefresh?: boolean;
  accessTokenType?: AccessTokenType;
  allowedIdentityTokenSigningAlgorithms?: string;
  includeJwtId?: boolean;

  // Client claims settings
  alwaysSendClientClaims?: boolean;
  clientClaimsPrefix?: string;
  pairWiseSubjectSalt?: string;

  // Logout settings
  frontChannelLogoutUri?: string;
  frontChannelLogoutSessionRequired?: boolean;
  backChannelLogoutUri?: string;
  backChannelLogoutSessionRequired?: boolean;

  // SSO and device settings
  enableLocalLogin?: boolean;
  userSsoLifetime?: number;
  userCodeType?: string;
  deviceCodeLifetime?: number;

  // CIBA settings
  cibaEnabled?: boolean;
  cibaTokenDeliveryMode?: string;
  cibaClientNotificationEndpoint?: string;
  cibaRequestLifetime?: number;
  cibaPollingInterval?: number;
  cibaRequireUserCode?: boolean;

  // UI Flow settings
  useJourneyFlow?: boolean | null;

  // Collections
  allowedGrantTypes?: string[];
  redirectUris?: string[];
  postLogoutRedirectUris?: string[];
  allowedScopes?: string[];
  allowedCorsOrigins?: string[];
  claims?: ClientClaim[];
  properties?: ClientProperty[];
  identityProviderRestrictions?: string[];
  allowedRoles?: string[];
  allowedUsers?: AllowedUser[];
}

export interface UpdateClientRequest {
  // Basic settings
  clientName?: string;
  description?: string;
  clientUri?: string;
  logoUri?: string;
  clientSecret?: string;
  enabled?: boolean;

  // Authentication settings
  requireClientSecret?: boolean;
  requirePkce?: boolean;
  allowPlainTextPkce?: boolean;
  requireRequestObject?: boolean;
  requireDPoP?: boolean;
  requirePushedAuthorization?: boolean;
  pushedAuthorizationLifetime?: number;

  // Consent settings
  requireConsent?: boolean;
  allowRememberConsent?: boolean;
  consentLifetime?: number;

  // Token settings
  allowOfflineAccess?: boolean;
  allowAccessTokensViaBrowser?: boolean;
  alwaysIncludeUserClaimsInIdToken?: boolean;
  accessTokenLifetime?: number;
  identityTokenLifetime?: number;
  authorizationCodeLifetime?: number;
  absoluteRefreshTokenLifetime?: number;
  slidingRefreshTokenLifetime?: number;
  refreshTokenUsage?: TokenUsage;
  refreshTokenExpiration?: TokenExpiration;
  updateAccessTokenClaimsOnRefresh?: boolean;
  accessTokenType?: AccessTokenType;
  allowedIdentityTokenSigningAlgorithms?: string;
  includeJwtId?: boolean;

  // Client claims settings
  alwaysSendClientClaims?: boolean;
  clientClaimsPrefix?: string;
  pairWiseSubjectSalt?: string;

  // Logout settings
  frontChannelLogoutUri?: string;
  frontChannelLogoutSessionRequired?: boolean;
  backChannelLogoutUri?: string;
  backChannelLogoutSessionRequired?: boolean;

  // SSO and device settings
  enableLocalLogin?: boolean;
  userSsoLifetime?: number;
  userCodeType?: string;
  deviceCodeLifetime?: number;

  // CIBA settings
  cibaEnabled?: boolean;
  cibaTokenDeliveryMode?: string;
  cibaClientNotificationEndpoint?: string;
  cibaRequestLifetime?: number;
  cibaPollingInterval?: number;
  cibaRequireUserCode?: boolean;

  // UI Flow settings
  useJourneyFlow?: boolean | null;

  // Collections
  allowedGrantTypes?: string[];
  redirectUris?: string[];
  postLogoutRedirectUris?: string[];
  allowedScopes?: string[];
  allowedCorsOrigins?: string[];
  claims?: ClientClaim[];
  properties?: ClientProperty[];
  identityProviderRestrictions?: string[];
  allowedRoles?: string[];
  allowedUsers?: AllowedUser[];
}
