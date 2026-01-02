export interface ApiResource {
  id: number;
  enabled: boolean;
  name: string;
  displayName?: string;
  description?: string;
  allowedAccessTokenSigningAlgorithms?: string;
  showInDiscoveryDocument: boolean;
  requireResourceIndicator: boolean;
  scopes: string[];
  secrets?: ApiResourceSecret[];
  userClaims: string[];
  properties?: ResourceProperty[];
  created: string;
  updated?: string;
  lastAccessed?: string;
}

export interface ApiResourceSecret {
  id: number;
  description?: string;
  value: string;
  expiration?: string;
  type: string;
  created: string;
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
  /** API resources this scope belongs to */
  apiResourceNames: string[];
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

export interface CreateApiResourceRequest {
  name: string;
  displayName?: string;
  description?: string;
  enabled?: boolean;
  showInDiscoveryDocument?: boolean;
  allowedAccessTokenSigningAlgorithms?: string;
  requireResourceIndicator?: boolean;
  scopes?: string[];
  userClaims?: string[];
}

export interface UpdateApiResourceRequest {
  displayName?: string;
  description?: string;
  enabled?: boolean;
  showInDiscoveryDocument?: boolean;
  allowedAccessTokenSigningAlgorithms?: string;
  requireResourceIndicator?: boolean;
  scopes?: string[];
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
  /** Optional: API resource to automatically add this scope to */
  apiResourceName?: string;
}

export interface UpdateApiScopeRequest {
  displayName?: string;
  description?: string;
  enabled?: boolean;
  required?: boolean;
  emphasize?: boolean;
  showInDiscoveryDocument?: boolean;
  userClaims?: string[];
  /** API resources this scope should belong to */
  apiResourceNames?: string[];
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
  enabled: boolean;
}

export interface ApiResourceSummary {
  name: string;
  displayName?: string;
  description?: string;
  enabled: boolean;
  scopeCount: number;
}
