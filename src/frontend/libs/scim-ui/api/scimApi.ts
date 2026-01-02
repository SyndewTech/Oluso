import type { AxiosInstance } from 'axios';

let apiClient: AxiosInstance | null = null;
let serverBaseUrl: string = '';

export function setApiClient(client: AxiosInstance) {
  apiClient = client;
}

export function setServerBaseUrl(url: string) {
  serverBaseUrl = url.replace(/\/$/, ''); // Remove trailing slash
}

export function getServerBaseUrl(): string {
  return serverBaseUrl;
}

export function getScimEndpointUrl(): string {
  return `${serverBaseUrl}/scim/v2`;
}

function getClient(): AxiosInstance {
  if (!apiClient) {
    throw new Error('SCIM API client not initialized. Call setApiClient first.');
  }
  return apiClient;
}

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
  createdAt: string;
  lastActivityAt?: string;
  successCount: number;
  errorCount: number;
}

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
}

export interface CreateScimClientResponse {
  client: ScimClient;
  token: string;
}

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

export interface ScimLogsResponse {
  total: number;
  skip: number;
  take: number;
  items: ScimProvisioningLog[];
}

/**
 * SCIM attribute mapping - maps SCIM attributes to internal user properties
 */
export interface ScimAttributeMapping {
  id: string;
  scimClientId: string;
  /** The SCIM attribute path (e.g., "userName", "emails[type eq \"work\"].value", "name.familyName") */
  scimAttribute: string;
  /** The internal user property to map to (e.g., "Email", "LastName", "Department") */
  internalProperty: string;
  /** Direction of the mapping: inbound (SCIM -> internal), outbound (internal -> SCIM), or both */
  direction: 'inbound' | 'outbound' | 'both';
  /** Whether this mapping is required for user creation */
  isRequired: boolean;
  /** Default value if the SCIM attribute is not provided */
  defaultValue?: string;
  /** Optional transformation function name to apply */
  transformation?: string;
  /** Priority for conflict resolution (higher = more priority) */
  priority: number;
  /** Whether this mapping is enabled */
  isEnabled: boolean;
  createdAt: string;
  updatedAt?: string;
}

export interface CreateScimAttributeMappingRequest {
  scimAttribute: string;
  internalProperty: string;
  direction?: 'inbound' | 'outbound' | 'both';
  isRequired?: boolean;
  defaultValue?: string;
  transformation?: string;
  priority?: number;
  isEnabled?: boolean;
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
  type: 'string' | 'boolean' | 'date' | 'array';
  description?: string;
}

export const scimApi = {
  // Client operations
  async getClients(): Promise<ScimClient[]> {
    const response = await getClient().get<ScimClient[]>('/scim/clients');
    return response.data;
  },

  async getClient(id: string): Promise<ScimClient> {
    const response = await getClient().get<ScimClient>(`/scim/clients/${id}`);
    return response.data;
  },

  async createClient(request: CreateScimClientRequest): Promise<CreateScimClientResponse> {
    const response = await getClient().post<CreateScimClientResponse>('/scim/clients', request);
    return response.data;
  },

  async updateClient(id: string, request: Partial<CreateScimClientRequest>): Promise<ScimClient> {
    const response = await getClient().put<ScimClient>(`/scim/clients/${id}`, request);
    return response.data;
  },

  async deleteClient(id: string): Promise<void> {
    await getClient().delete(`/scim/clients/${id}`);
  },

  async rotateToken(id: string): Promise<{ token: string }> {
    const response = await getClient().post<{ token: string }>(`/scim/clients/${id}/rotate-token`);
    return response.data;
  },

  async getLogs(clientId: string, skip = 0, take = 50): Promise<ScimLogsResponse> {
    const response = await getClient().get<ScimLogsResponse>(
      `/scim/clients/${clientId}/logs`,
      { params: { skip, take } }
    );
    return response.data;
  },

  // Attribute mapping operations
  async getMappings(clientId: string): Promise<ScimAttributeMapping[]> {
    const response = await getClient().get<ScimAttributeMapping[]>(
      `/scim/clients/${clientId}/mappings`
    );
    return response.data;
  },

  async getMapping(clientId: string, mappingId: string): Promise<ScimAttributeMapping> {
    const response = await getClient().get<ScimAttributeMapping>(
      `/scim/clients/${clientId}/mappings/${mappingId}`
    );
    return response.data;
  },

  async createMapping(
    clientId: string,
    request: CreateScimAttributeMappingRequest
  ): Promise<ScimAttributeMapping> {
    const response = await getClient().post<ScimAttributeMapping>(
      `/scim/clients/${clientId}/mappings`,
      request
    );
    return response.data;
  },

  async updateMapping(
    clientId: string,
    mappingId: string,
    request: Partial<CreateScimAttributeMappingRequest>
  ): Promise<ScimAttributeMapping> {
    const response = await getClient().put<ScimAttributeMapping>(
      `/scim/clients/${clientId}/mappings/${mappingId}`,
      request
    );
    return response.data;
  },

  async deleteMapping(clientId: string, mappingId: string): Promise<void> {
    await getClient().delete(`/scim/clients/${clientId}/mappings/${mappingId}`);
  },

  async applyDefaultMappings(clientId: string, provider?: string): Promise<ScimAttributeMapping[]> {
    const response = await getClient().post<ScimAttributeMapping[]>(
      `/scim/clients/${clientId}/mappings/defaults`,
      { provider }
    );
    return response.data;
  },

  // Suggestions and options
  getScimAttributeSuggestions(): ScimAttributeSuggestion[] {
    return [
      // Core SCIM User attributes
      { attribute: 'userName', description: 'Unique username for the user', provider: 'standard' },
      { attribute: 'name.givenName', description: 'First name', provider: 'standard' },
      { attribute: 'name.familyName', description: 'Last name', provider: 'standard' },
      { attribute: 'name.formatted', description: 'Full formatted name', provider: 'standard' },
      { attribute: 'displayName', description: 'Display name', provider: 'standard' },
      { attribute: 'emails[type eq "work"].value', description: 'Work email address', provider: 'standard' },
      { attribute: 'emails[primary eq true].value', description: 'Primary email address', provider: 'standard' },
      { attribute: 'phoneNumbers[type eq "work"].value', description: 'Work phone number', provider: 'standard' },
      { attribute: 'phoneNumbers[type eq "mobile"].value', description: 'Mobile phone number', provider: 'standard' },
      { attribute: 'active', description: 'Whether the user is active', provider: 'standard' },
      { attribute: 'title', description: 'Job title', provider: 'standard' },
      { attribute: 'preferredLanguage', description: 'Preferred language', provider: 'standard' },
      { attribute: 'locale', description: 'Locale', provider: 'standard' },
      { attribute: 'timezone', description: 'Timezone', provider: 'standard' },
      { attribute: 'externalId', description: 'External identifier', provider: 'standard' },

      // Enterprise User extension
      { attribute: 'urn:ietf:params:scim:schemas:extension:enterprise:2.0:User:employeeNumber', description: 'Employee number', provider: 'standard' },
      { attribute: 'urn:ietf:params:scim:schemas:extension:enterprise:2.0:User:department', description: 'Department', provider: 'standard' },
      { attribute: 'urn:ietf:params:scim:schemas:extension:enterprise:2.0:User:manager.value', description: 'Manager user ID', provider: 'standard' },
      { attribute: 'urn:ietf:params:scim:schemas:extension:enterprise:2.0:User:organization', description: 'Organization', provider: 'standard' },
      { attribute: 'urn:ietf:params:scim:schemas:extension:enterprise:2.0:User:costCenter', description: 'Cost center', provider: 'standard' },
      { attribute: 'urn:ietf:params:scim:schemas:extension:enterprise:2.0:User:division', description: 'Division', provider: 'standard' },

      // Azure AD specific
      { attribute: 'urn:ietf:params:scim:schemas:extension:azure:2.0:User:objectId', description: 'Azure AD Object ID', provider: 'azure' },
      { attribute: 'urn:ietf:params:scim:schemas:extension:azure:2.0:User:immutableId', description: 'Azure AD Immutable ID', provider: 'azure' },

      // Okta specific
      { attribute: 'urn:ietf:params:scim:schemas:extension:okta:2.0:User:secondEmail', description: 'Okta secondary email', provider: 'okta' },
    ];
  },

  getInternalPropertyOptions(): InternalPropertyOption[] {
    return [
      { name: 'UserName', displayName: 'Username', type: 'string', description: 'Login username' },
      { name: 'Email', displayName: 'Email', type: 'string', description: 'Primary email address' },
      { name: 'FirstName', displayName: 'First Name', type: 'string' },
      { name: 'LastName', displayName: 'Last Name', type: 'string' },
      { name: 'DisplayName', displayName: 'Display Name', type: 'string' },
      { name: 'PhoneNumber', displayName: 'Phone Number', type: 'string' },
      { name: 'IsActive', displayName: 'Is Active', type: 'boolean' },
      { name: 'JobTitle', displayName: 'Job Title', type: 'string' },
      { name: 'Department', displayName: 'Department', type: 'string' },
      { name: 'Organization', displayName: 'Organization', type: 'string' },
      { name: 'EmployeeId', displayName: 'Employee ID', type: 'string' },
      { name: 'ManagerId', displayName: 'Manager ID', type: 'string' },
      { name: 'PreferredLanguage', displayName: 'Preferred Language', type: 'string' },
      { name: 'Timezone', displayName: 'Timezone', type: 'string' },
      { name: 'ExternalId', displayName: 'External ID', type: 'string' },
      { name: 'ProfilePictureUrl', displayName: 'Profile Picture URL', type: 'string' },
      { name: 'CustomAttributes', displayName: 'Custom Attributes (JSON)', type: 'string' },
    ];
  },

  getTransformationOptions(): { name: string; description: string }[] {
    return [
      { name: 'none', description: 'No transformation' },
      { name: 'lowercase', description: 'Convert to lowercase' },
      { name: 'uppercase', description: 'Convert to uppercase' },
      { name: 'trim', description: 'Trim whitespace' },
      { name: 'normalize_email', description: 'Normalize email address' },
      { name: 'normalize_phone', description: 'Normalize phone number' },
      { name: 'extract_domain', description: 'Extract domain from email' },
      { name: 'format_name', description: 'Format as proper name (Title Case)' },
    ];
  },
};
