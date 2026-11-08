export interface ApiScopeDto {
  id: number;
  enabled: boolean;
  name: string;
  displayName?: string;
  description?: string;
  required: boolean;
  emphasize: boolean;
  showInDiscoveryDocument: boolean;
  userClaims: string[];
  properties?: ResourcePropertyDto[];
  created: string;
  updated?: string;
  /** Platform-managed scope — the update and delete endpoints reject it. */
  nonEditable?: boolean;
  /** Matches any scope value that starts with this name; the client chooses the rest (the suffix). */
  isPrefix?: boolean;
  /** Prefix scopes: a regular expression the whole suffix must match. Empty: any characters a scope can contain. */
  suffixPattern?: string | null;
  /** Prefix scopes: the longest suffix accepted. Empty: the server default. */
  suffixMaxLength?: number | null;
}

export interface ResourcePropertyDto {
  id: number;
  key: string;
  value: string;
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
  isPrefix?: boolean;
  suffixPattern?: string | null;
  suffixMaxLength?: number | null;
}

export interface ApiScopeSummaryDto {
  name: string;
  displayName?: string;
  description?: string;
}
