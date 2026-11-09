/** One grantable Service API permission. */
export interface ServiceApiScopeDto {
  name: string;
  displayName: string;
  description: string;
  /** Grouping key for the UI (e.g. "Users", "Roles"). */
  group: string;
  /**
   * Sensitive: only an admin holding `requiredAdminPermission` (granted by name,
   * never by wildcard) can give it to a client. Never pre-selected.
   */
  requiresExplicitGrant: boolean;
  requiredAdminPermission?: string | null;
}

/** The Service API permissions a client can be granted. */
export interface ServiceApiCatalogDto {
  /** Scope that turns Service API access on. */
  baseScope: string;
  /** Grant types the client must allow for the scopes to be usable. */
  requiredGrantTypes: string[];
  scopes: ServiceApiScopeDto[];
}
