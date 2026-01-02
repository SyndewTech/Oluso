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

export interface UploadPluginRequest {
  file: File;
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
  requiredClaims?: string[];
  outputClaims?: string[];
  configSchema?: Record<string, unknown>;
}
