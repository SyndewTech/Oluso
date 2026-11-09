// String values must match the C# enum member names of
// Oluso.Core.Domain.Entities.ExternalProviderType. The platform host
// registers JsonStringEnumConverter globally, so the API ships and
// accepts the enum as its name string (e.g. "Microsoft", "Google").
// Comparing against integers — what we used to do here — silently
// failed because the wire format never contained numbers.
export type ExternalProviderType =
  | 'Google'
  | 'Microsoft'
  | 'Facebook'
  | 'Apple'
  | 'GitHub'
  | 'LinkedIn'
  | 'Twitter'
  | 'Oidc'
  | 'OAuth2'
  | 'Saml2'
  | 'Ldap';

export const ProviderTypes = {
  Google: 'Google',
  Microsoft: 'Microsoft',
  Facebook: 'Facebook',
  Apple: 'Apple',
  GitHub: 'GitHub',
  LinkedIn: 'LinkedIn',
  Twitter: 'Twitter',
  Oidc: 'Oidc',
  OAuth2: 'OAuth2',
  Saml2: 'Saml2',
  Ldap: 'Ldap',
} as const satisfies Record<string, ExternalProviderType>;

export interface ProviderTypeInfo {
  type: ExternalProviderType;
  name: string;
  description?: string;
  category?: string;
}

export interface IdentityProvider {
  id: number;
  scheme: string;
  displayName?: string;
  enabled: boolean;
  providerType: ExternalProviderType;
  iconUrl?: string;
  displayOrder?: number;
  allowedClientIds: string[];
  nonEditable?: boolean;
  created: string;
  updated?: string;
  lastAccessed?: string;
}

export interface IdentityProviderEdit extends IdentityProvider {
  configuration: Record<string, unknown>;
}

export interface CreateIdentityProviderRequest {
  scheme: string;
  displayName?: string;
  enabled: boolean;
  providerType: ExternalProviderType;
  iconUrl?: string;
  displayOrder?: number;
  allowedClientIds?: string[];
  configuration: Record<string, unknown>;
}

export interface SchemeAvailability {
  available: boolean;
  message?: string;
}

export interface ServerInfo {
  issuerUri?: string;
  callbackPathTemplate?: string;
}

/** An icon found on the provider's site, already re-encoded server-side as a PNG data URL. */
export interface ProviderIconCandidate {
  source: string;
  sourceUrl: string;
  dataUrl: string;
  width: number;
  height: number;
  sourceFormat: string;
}

export interface ProviderIconDiscovery {
  origin?: string;
  candidates: ProviderIconCandidate[];
  notes: string[];
}
