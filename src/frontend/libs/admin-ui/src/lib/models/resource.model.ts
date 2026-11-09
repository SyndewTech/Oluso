/**
 * RFC 8707 Resource - Protected resource identified by absolute URI
 */
export interface ResourceDto {
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

export interface ResourceSummaryDto {
  uri: string;
  displayName?: string;
  description?: string;
  enabled: boolean;
  scopeCount: number;
}
