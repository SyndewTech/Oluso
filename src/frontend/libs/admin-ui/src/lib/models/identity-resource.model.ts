export interface IdentityResourceDto {
  id: number;
  enabled: boolean;
  name: string;
  displayName?: string;
  description?: string;
  required: boolean;
  emphasize: boolean;
  showInDiscoveryDocument: boolean;
  userClaims: string[];
  created: string;
  updated?: string;
  /** A standard OIDC scope the server provides itself (id 0, not a registered row). */
  builtIn?: boolean;
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
