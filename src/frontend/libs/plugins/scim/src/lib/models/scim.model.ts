/**
 * SCIM Client configuration
 */
export interface ScimClient {
  id: string;
  name: string;
  description?: string;
  isEnabled: boolean;
  tokenCreatedAt: string;
  tokenExpiresAt?: string;
  allowedIpRanges?: string;
  rateLimitPerMinute: number;
  canCreateUsers: boolean;
  canUpdateUsers: boolean;
  canDeleteUsers: boolean;
  canManageGroups: boolean;
  defaultRoleId?: string;
  /** Optional OAuth client (clientId) linked for client-credentials JWT auth. */
  oauthClientId?: string;
  createdAt: string;
  lastActivityAt?: string;
  successCount: number;
  errorCount: number;
}

/**
 * Request to create a new SCIM client
 */
export interface CreateScimClientRequest {
  name: string;
  description?: string;
  isEnabled?: boolean;
  tokenExpiresAt?: string;
  allowedIpRanges?: string;
  rateLimitPerMinute?: number;
  canCreateUsers?: boolean;
  canUpdateUsers?: boolean;
  canDeleteUsers?: boolean;
  canManageGroups?: boolean;
  defaultRoleId?: string;
  oauthClientId?: string;
}

/**
 * Response when creating a SCIM client (includes the token)
 */
export interface CreateScimClientResponse {
  client: ScimClient;
  token: string;
}

/**
 * SCIM provisioning log entry
 */
export interface ScimProvisioningLog {
  id: string;
  method: string;
  path: string;
  resourceType?: string;
  resourceId?: string;
  operation: string;
  statusCode: number;
  success: boolean;
  errorMessage?: string;
  clientIp?: string;
  durationMs: number;
  timestamp: string;
}

/**
 * Inbound attribute mapping: how an attribute of the SCIM resource an
 * identity provider sends is written onto the internal user.
 */
export interface ScimAttributeMapping {
  id: string;
  scimClientId: string;
  /** The SCIM attribute path (e.g., "userName", "emails[type eq \"work\"].value", "name.familyName") */
  scimAttribute: string;
  /** The internal user property to write (e.g., "Email", "LastName", "custom:department") */
  internalProperty: string;
  /** Scriban template computing the value; read from the attribute path when empty. Model: scim, enterprise, attr, client. */
  expression?: string;
  /** Scriban boolean expression; the mapping is skipped when false */
  condition?: string;
  /** Whether this mapping must resolve when a user is created */
  isRequired: boolean;
  /** Default value if the SCIM attribute is not provided */
  defaultValue?: string;
  /** Optional transformation function name to apply */
  transformation?: string;
  /** Among mappings writing the same property, higher priority is tried first; the first with a value wins */
  priority: number;
  /** Whether this mapping is enabled */
  isEnabled: boolean;
  createdAt: string;
  updatedAt?: string;
}

/**
 * Request to create or update a SCIM attribute mapping
 */
export interface SaveScimAttributeMappingRequest {
  scimAttribute: string;
  internalProperty: string;
  expression?: string;
  condition?: string;
  isRequired?: boolean;
  defaultValue?: string;
  transformation?: string;
  priority?: number;
  isEnabled?: boolean;
}

/** @deprecated use SaveScimAttributeMappingRequest */
export type CreateScimAttributeMappingRequest = SaveScimAttributeMappingRequest;

export interface ScimMappingOptions {
  internalProperties: string[];
  transformations: string[];
  presets: string[];
  /** The mappings in force when a client has none configured */
  defaults: ScimAttributeMapping[];
}

export interface ScimMappingPreview {
  success: boolean;
  error?: string;
  /** Internal property to the value that would be written; null means cleared */
  fields: Record<string, string | null>;
  usingDefaults: boolean;
}

/**
 * Predefined SCIM attribute suggestions for common providers
 */
export interface ScimAttributeSuggestion {
  attribute: string;
  description: string;
  example?: string;
  provider?: 'azure' | 'okta' | 'google' | 'standard';
}

/**
 * Internal user property options
 */
export interface InternalPropertyOption {
  name: string;
  displayName: string;
}

/**
 * Transformation option for attribute mapping
 */
export interface TransformationOption {
  name: string;
  description: string;
}
