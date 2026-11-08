/**
 * Outbound SCIM provider: a downstream system this tenant pushes users
 * (and optionally groups) to. The counterpart of ScimClient, which is an
 * identity provider that pushes into us.
 */
export interface ScimProvider {
  id: string;
  name: string;
  description?: string;
  isEnabled: boolean;
  flavor: string;
  baseUrl: string;
  authType: ScimProviderAuthType;
  oauthTokenEndpoint?: string;
  oauthClientId?: string;
  oauthScope?: string;
  /** A secret is stored. The value itself is never returned. */
  hasSecret: boolean;
  secretUpdatedAt?: string;
  syncUsers: boolean;
  syncGroups: boolean;
  /** What a SCIM Group is built from. */
  groupSource: ScimProviderGroupSource;
  scopeRoleIds: string[];
  /** Team membership includes sub-teams. */
  scopeTeamIds: string[];
  userNameSource: ScimProviderUserNameSource;
  /** Scriban template used when userNameSource is 'expression'. */
  userNameExpression?: string;
  deprovisionMode: ScimProviderDeprovisionMode;
  reconcileIntervalMinutes: number;
  /** Inbound SCIM client that is this same system; its changes are not pushed back here. */
  linkedScimClientId?: string;
  createdAt: string;
  updatedAt?: string;
  lastReconcileAt?: string;
  lastReconcileSummary?: string;
  lastSuccessAt?: string;
  lastErrorAt?: string;
  lastError?: string;
  successCount: number;
  errorCount: number;
}

export type ScimProviderAuthType = 'bearer' | 'oauth_client_credentials';
export type ScimProviderUserNameSource = 'userName' | 'email' | 'expression';
export type ScimProviderDeprovisionMode = 'deactivate' | 'delete';
export type ScimProviderGroupSource = 'roles' | 'teams' | 'both';

export interface CreateScimProviderRequest {
  name: string;
  description?: string;
  isEnabled?: boolean;
  flavor: string;
  baseUrl: string;
  authType: ScimProviderAuthType;
  oauthTokenEndpoint?: string;
  oauthClientId?: string;
  oauthScope?: string;
  secret?: string;
  syncUsers?: boolean;
  syncGroups?: boolean;
  groupSource?: ScimProviderGroupSource;
  scopeRoleIds?: string[];
  scopeTeamIds?: string[];
  userNameSource?: ScimProviderUserNameSource;
  userNameExpression?: string;
  deprovisionMode?: ScimProviderDeprovisionMode;
  reconcileIntervalMinutes?: number;
  linkedScimClientId?: string;
}

export interface UpdateScimProviderRequest {
  name: string;
  description?: string;
  isEnabled: boolean;
  flavor: string;
  baseUrl: string;
  authType: ScimProviderAuthType;
  oauthTokenEndpoint?: string;
  oauthClientId?: string;
  oauthScope?: string;
  syncUsers: boolean;
  syncGroups: boolean;
  groupSource: ScimProviderGroupSource;
  scopeRoleIds: string[];
  scopeTeamIds: string[];
  userNameSource: ScimProviderUserNameSource;
  userNameExpression?: string;
  deprovisionMode: ScimProviderDeprovisionMode;
  reconcileIntervalMinutes: number;
  linkedScimClientId?: string;
}

export interface ScimProviderFlavor {
  key: string;
  displayName: string;
  description: string;
  notes?: string;
  defaultUserNameSource: ScimProviderUserNameSource;
  requiresEmailUserName: boolean;
  supportsPhoneNumbers: boolean;
  supportsEnterpriseExtension: boolean;
  supportsGroups: boolean;
  defaultUserMappings: { scimAttribute: string; internalProperty: string }[];
}

export interface ScimProviderOptions {
  flavors: ScimProviderFlavor[];
  internalProperties: string[];
  transformations: string[];
  authTypes: ScimProviderAuthType[];
  userNameSources: ScimProviderUserNameSource[];
  deprovisionModes: ScimProviderDeprovisionMode[];
  groupSources: ScimProviderGroupSource[];
}

export interface ScimProviderAttributeMapping {
  id: string;
  scimProviderId: string;
  scimAttribute: string;
  /** Empty when the value comes from an expression. */
  internalProperty: string;
  /** Scriban template computing the value; takes precedence over internalProperty. */
  expression?: string;
  /** Scriban boolean expression; the attribute is omitted when false. */
  condition?: string;
  transformation?: string;
  defaultValue?: string;
  isEnabled: boolean;
  createdAt: string;
  updatedAt?: string;
}

export interface SaveScimProviderAttributeMappingRequest {
  scimAttribute: string;
  internalProperty?: string;
  expression?: string;
  condition?: string;
  transformation?: string;
  defaultValue?: string;
  isEnabled: boolean;
}

export interface ScimProviderPreview {
  userId: string;
  userName?: string;
  inScope: boolean;
  error?: string;
  payload?: Record<string, unknown>;
}

export interface ScimProviderTestResult {
  success: boolean;
  message: string;
  discoveryReachable: boolean;
  discoveryStatusCode: number;
  authenticationOk: boolean;
  authenticationStatusCode: number;
  authenticationError?: string;
  patchSupported?: boolean;
  bulkSupported?: boolean;
  filterSupported?: boolean;
  filterMaxResults?: number;
  etagSupported?: boolean;
  authenticationSchemes: string[];
}

export interface ScimProviderReconcileResult {
  usersQueued: number;
  userRemovalsQueued: number;
  groupsQueued: number;
  groupRemovalsQueued: number;
  summary: string;
}

export type ScimQueueStatus = 'pending' | 'processing' | 'succeeded' | 'failed' | 'dead' | 'superseded';

export interface ScimProviderQueueSummary {
  pending: number;
  processing: number;
  failed: number;
  dead: number;
  succeededLast24h: number;
  oldestPendingAt?: string;
}

export interface ScimProviderQueueItem {
  id: string;
  resourceType: 'User' | 'Group';
  internalId: string;
  operation: 'upsert' | 'remove';
  status: ScimQueueStatus;
  attempts: number;
  nextAttemptAt: string;
  lastStatusCode?: number;
  lastError?: string;
  reason?: string;
  createdAt: string;
  updatedAt?: string;
  completedAt?: string;
}

export interface ScimProviderQueueResponse {
  summary: ScimProviderQueueSummary;
  total: number;
  skip: number;
  take: number;
  items: ScimProviderQueueItem[];
}

export interface ScimProviderLog {
  id: string;
  operation: string;
  resourceType?: string;
  internalId?: string;
  remoteId?: string;
  method: string;
  path: string;
  statusCode: number;
  success: boolean;
  errorMessage?: string;
  durationMs: number;
  timestamp: string;
}

export interface ScimProviderResource {
  id: string;
  resourceType: 'User' | 'Group';
  internalId: string;
  remoteId: string;
  remoteVersion?: string;
  isDeprovisioned: boolean;
  createdAt: string;
  lastSyncedAt?: string;
}

