/**
 * RFC 8707 Resource - Protected resource identified by absolute URI
 */
export interface Resource {
  id: number;
  /** The absolute URI identifying this resource (RFC 8707) */
  uri: string;
  enabled: boolean;
  displayName?: string;
  description?: string;
  showInDiscoveryDocument: boolean;
  /** Scopes that are valid for this resource */
  allowedScopes: string[];
  userClaims: string[];
  created: string;
  updated?: string;
}

export interface ApiScope {
  id: number;
  enabled: boolean;
  name: string;
  displayName?: string;
  description?: string;
  required: boolean;
  emphasize: boolean;
  showInDiscoveryDocument: boolean;
  userClaims: string[];
  properties?: ResourceProperty[];
  created: string;
  updated?: string;
}

export interface IdentityResource {
  id: number;
  enabled: boolean;
  name: string;
  displayName?: string;
  description?: string;
  required: boolean;
  emphasize: boolean;
  showInDiscoveryDocument: boolean;
  userClaims: string[];
  properties?: ResourceProperty[];
  created: string;
  updated?: string;
}

export interface ResourceProperty {
  id: number;
  key: string;
  value: string;
}

export interface CreateResourceRequest {
  /** The absolute URI identifying this resource (RFC 8707) */
  uri: string;
  displayName?: string;
  description?: string;
  enabled?: boolean;
  showInDiscoveryDocument?: boolean;
  allowedScopes?: string[];
  userClaims?: string[];
}

export interface UpdateResourceRequest {
  displayName?: string;
  description?: string;
  enabled?: boolean;
  showInDiscoveryDocument?: boolean;
  allowedScopes?: string[];
  userClaims?: string[];
}

export interface CreateApiScopeRequest {
  name: string;
  displayName?: string;
  description?: string;
  enabled?: boolean;
  required?: boolean;
  emphasize?: boolean;
  showInDiscoveryDocument?: boolean;
  userClaims?: string[];
}

export interface UpdateApiScopeRequest {
  displayName?: string;
  description?: string;
  enabled?: boolean;
  required?: boolean;
  emphasize?: boolean;
  showInDiscoveryDocument?: boolean;
  userClaims?: string[];
}

export interface CreateIdentityResourceRequest {
  name: string;
  displayName?: string;
  description?: string;
  enabled?: boolean;
  required?: boolean;
  emphasize?: boolean;
  showInDiscoveryDocument?: boolean;
  userClaims?: string[];
}

export interface ApiScopeSummary {
  name: string;
  displayName?: string;
  description?: string;
}

export interface ResourceSummary {
  uri: string;
  displayName?: string;
  description?: string;
  enabled: boolean;
  scopeCount: number;
}
