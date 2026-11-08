import { ClientClaim } from './client-claim.model';
import { ClientAuthorizeParameter } from './client-authorize-parameter.model';
import { ClientProperty } from './client-property.model';
import { AllowedUser } from './allowed-user.model';
import { TokenUsage, TokenExpiration, AccessTokenType } from './token-enums.model';

export interface ClientRequest {
  // Required
  clientId: string;

  // Basic settings
  clientName?: string;
  description?: string;
  clientUri?: string;
  logoUri?: string;
  /** Only used for create - secrets are managed via regenerateSecret/removeClientSecret for existing clients */
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
  resetUseJourneyFlowToInherit?: boolean;

  // Access restrictions
  requiredPermission?: string | null;
  requireVerifiedEmail?: boolean;

  // Collections
  allowedGrantTypes?: string[];
  redirectUris?: string[];
  postLogoutRedirectUris?: string[];
  allowedScopes?: string[];
  allowedCorsOrigins?: string[];
  claims?: ClientClaim[];
  allowedAuthorizeParameters?: ClientAuthorizeParameter[];
  properties?: ClientProperty[];
  identityProviderRestrictions?: string[];
  /** Resource indicators (RFC 8707) the client may request tokens for; empty means any. */
  allowedResources?: string[];
  /** Resources this client is the resource server for; it may introspect access tokens issued for them. */
  introspectionResources?: string[];
  allowedRoles?: string[];
  allowedUsers?: AllowedUser[];
}
