export interface Plugin {
  name: string;
  displayName: string;
  description?: string;
  version?: string;
  author?: string;
  scope: 'Global' | 'Tenant';
  tenantId?: string;
  sizeBytes: number;
  createdAt: string;
  updatedAt?: string;
  requiredClaims?: string[];
  outputClaims?: string[];
  configSchema?: Record<string, unknown>;
  isLoaded: boolean;
}

export interface PluginListItem {
  name: string;
  displayName: string;
  description?: string;
  version?: string;
  author?: string;
  scope: string;
  isGlobal: boolean;
  sizeBytes: number;
  createdAt: string;
  updatedAt?: string;
  isLoaded: boolean;
}

export interface AvailablePlugin {
  name: string;
  displayName: string;
  description?: string;
  version?: string;
  author?: string;
  scope: string;
  isGlobal: boolean;
  requiredClaims?: string[];
  outputClaims?: string[];
  configSchema?: Record<string, unknown>;
}

export interface UploadPluginMetadata {
  name?: string;
  displayName?: string;
  description?: string;
  version?: string;
  author?: string;
  requiredClaims?: string[];
  outputClaims?: string[];
  configSchema?: Record<string, unknown>;
}

export interface UpdatePluginMetadataRequest {
  displayName?: string;
  description?: string;
  version?: string;
  author?: string;
  enabled?: boolean;
  requiredClaims?: string[];
  outputClaims?: string[];
  configSchema?: Record<string, unknown>;
}

// ============ Hook Bindings ============

export interface PluginHookBinding {
  id: string;
  pluginId: string;
  domain: string;
  entityType: string;
  eventType: string;
  functionName: string;
  order: number;
  isActive: boolean;
  failureMode: 'Continue' | 'Reject';
  timeoutMs: number;
  filterConditions?: string;
}

export interface CreateHookBindingRequest {
  domain: string;
  entityType: string;
  eventType: string;
  functionName: string;
  order?: number;
  isActive?: boolean;
  failureMode?: 'Continue' | 'Reject';
  timeoutMs?: number;
  filterConditions?: string;
}

export interface UpdateHookBindingRequest {
  domain: string;
  entityType: string;
  eventType: string;
  functionName: string;
  order?: number;
  isActive?: boolean;
  failureMode?: 'Continue' | 'Reject';
  timeoutMs?: number;
  filterConditions?: string;
}

// ============ Execution Logs ============

export interface PluginExecutionLog {
  id: string;
  pluginId: string;
  hookBindingId?: string;
  executionId: string;
  domain: string;
  eventType: string;
  entityType?: string;
  entityId?: string;
  durationMs: number;
  result: 'Success' | 'Error' | 'Timeout' | 'Skipped';
  resultAction?: string;
  errorMessage?: string;
  userId?: string;
  executedAt: string;
}

// ============ Network Configuration ============

export type NetworkAccessMode = 'None' | 'Self' | 'Whitelist' | 'All';

export interface PluginNetworkConfig {
  pluginId: string;
  pluginName: string;
  accessMode: NetworkAccessMode;
  allowedHosts: string[];
  httpTimeoutMs: number;
  maxHttpRequestsPerExecution: number;
  requiresApproval: boolean;
  isApproved: boolean;
  approvedBy?: string;
  approvedAt?: string;
}

export interface UpdateNetworkConfigRequest {
  accessMode?: NetworkAccessMode;
  allowedHosts?: string[];
  httpTimeoutMs?: number;
  maxHttpRequestsPerExecution?: number;
}
