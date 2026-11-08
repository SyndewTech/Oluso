/**
 * SAML Service Provider model
 */
export interface SamlServiceProvider {
  id: number;
  entityId: string;
  displayName?: string;
  description?: string;
  enabled: boolean;
  metadataUrl?: string;
  assertionConsumerServiceUrl?: string;
  singleLogoutServiceUrl?: string;
  hasSigningCertificate: boolean;
  hasEncryptionCertificate: boolean;
  encryptAssertions: boolean;
  nameIdFormat?: string;
  requireVerifiedEmail: boolean;
  allowedClaims?: string[];
  claimMappings?: Record<string, string>;
  ssoBinding: string;
  signResponses: boolean;
  signAssertions: boolean;
  requireSignedAuthnRequests: boolean;
  defaultRelayState?: string;
  nonEditable: boolean;
  created: string;
  updated?: string;
  lastAccessed?: string;
}

/**
 * SAML Service Provider for editing (includes certificates)
 */
export interface SamlServiceProviderEdit {
  id: number;
  entityId: string;
  displayName?: string;
  description?: string;
  enabled: boolean;
  metadataUrl?: string;
  assertionConsumerServiceUrl?: string;
  singleLogoutServiceUrl?: string;
  signingCertificate?: string;
  encryptionCertificate?: string;
  encryptAssertions: boolean;
  nameIdFormat?: string;
  requireVerifiedEmail: boolean;
  allowedClaims?: string[];
  claimMappings?: Record<string, string>;
  ssoBinding: string;
  signResponses: boolean;
  signAssertions: boolean;
  requireSignedAuthnRequests: boolean;
  defaultRelayState?: string;
  nonEditable: boolean;
}

/**
 * SAML Identity Provider info
 */
export interface SamlIdpInfo {
  enabled: boolean;
  entityId: string;
  singleSignOnServiceUrl: string;
  singleLogoutServiceUrl: string;
  metadataUrl: string;
  supportedNameIdFormats: string[];
}

/**
 * Request to create a SAML Service Provider
 */
export interface CreateSamlServiceProviderRequest {
  entityId: string;
  displayName?: string;
  description?: string;
  enabled?: boolean;
  metadataUrl?: string;
  assertionConsumerServiceUrl?: string;
  singleLogoutServiceUrl?: string;
  signingCertificate?: string;
  encryptionCertificate?: string;
  encryptAssertions?: boolean;
  nameIdFormat?: string;
  requireVerifiedEmail?: boolean;
  allowedClaims?: string[];
  claimMappings?: Record<string, string>;
  ssoBinding?: string;
  signResponses?: boolean;
  signAssertions?: boolean;
  requireSignedAuthnRequests?: boolean;
  defaultRelayState?: string;
}

/**
 * Test result for SAML configuration
 */
export interface SamlTestResult {
  success: boolean;
  message: string;
}

/**
 * SAML IdP Configuration
 */
export interface SamlIdpConfiguration {
  enabled: boolean;
  loginJourneyName?: string;
}

/**
 * SAML Certificate info
 */
export interface SamlCertificateInfo {
  source: 'Global' | 'Auto' | 'Uploaded';
  certificateId?: string;
  subject?: string;
  issuer?: string;
  notBefore?: string;
  notAfter?: string;
  thumbprint?: string;
  status: string;
  isRevoked: boolean;
  isExpired: boolean;
  isExpiringSoon: boolean;
  hasCertificate: boolean;
}

/**
 * Request to upload a certificate
 */
export interface UploadCertificateRequest {
  base64Pfx: string;
  password?: string;
}

/**
 * Entity ID availability check result
 */
export interface EntityIdAvailability {
  available: boolean;
  message?: string;
}

/**
 * Name ID format options
 */
export const NAME_ID_FORMATS = [
  { value: '', label: 'Default (emailAddress)' },
  { value: 'urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress', label: 'Email Address' },
  { value: 'urn:oasis:names:tc:SAML:2.0:nameid-format:persistent', label: 'Persistent' },
  { value: 'urn:oasis:names:tc:SAML:2.0:nameid-format:transient', label: 'Transient' },
  { value: 'urn:oasis:names:tc:SAML:1.1:nameid-format:unspecified', label: 'Unspecified' },
];

/**
 * SSO Binding options
 */
export const SSO_BINDINGS = [
  { value: 'POST', label: 'HTTP-POST' },
  { value: 'Redirect', label: 'HTTP-Redirect' },
];
