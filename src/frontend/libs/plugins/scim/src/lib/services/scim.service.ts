import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '@oluso/admin-ui';
import type { PaginatedResponse } from '@oluso/ui-core';
import {
  ScimClient,
  CreateScimClientRequest,
  CreateScimClientResponse,
  ScimProvisioningLog,
  ScimAttributeMapping,
  SaveScimAttributeMappingRequest,
  ScimMappingOptions,
  ScimMappingPreview,
  ScimAttributeSuggestion,
  InternalPropertyOption,
  TransformationOption,
} from '../models/scim.model';
import {
  ScimProvider,
  ScimProviderOptions,
  CreateScimProviderRequest,
  UpdateScimProviderRequest,
  ScimProviderAttributeMapping,
  SaveScimProviderAttributeMappingRequest,
  ScimProviderTestResult,
  ScimProviderReconcileResult,
  ScimProviderQueueResponse,
  ScimProviderLog,
  ScimProviderResource,
  ScimProviderPreview,
} from '../models/scim-provider.model';

@Injectable({
  providedIn: 'root',
})
export class ScimService {
  private readonly api = inject(ApiService);

  // ============================================
  // Client Operations
  // ============================================

  getClients(): Observable<ScimClient[]> {
    return this.api.get<ScimClient[]>('/scim/clients');
  }

  getClient(id: string): Observable<ScimClient> {
    return this.api.get<ScimClient>(`/scim/clients/${id}`);
  }

  createClient(request: CreateScimClientRequest): Observable<CreateScimClientResponse> {
    return this.api.post<CreateScimClientResponse>('/scim/clients', request);
  }

  updateClient(id: string, request: ScimClient): Observable<ScimClient> {
    return this.api.put<ScimClient>(`/scim/clients/${id}`, request);
  }

  deleteClient(id: string): Observable<void> {
    return this.api.delete<void>(`/scim/clients/${id}`);
  }

  rotateToken(id: string): Observable<{ token: string }> {
    return this.api.post<{ token: string }>(`/scim/clients/${id}/rotate-token`, {});
  }

  getLogs(
    clientId: string,
    page = 1,
    pageSize = 50,
  ): Observable<PaginatedResponse<ScimProvisioningLog>> {
    return this.api.getPaginated<ScimProvisioningLog>(`/scim/clients/${clientId}/logs`, {
      page,
      pageSize,
    });
  }

  // ============================================
  // Attribute Mapping Operations
  // ============================================

  getMappings(clientId: string): Observable<ScimAttributeMapping[]> {
    return this.api.get<ScimAttributeMapping[]>(`/scim/clients/${clientId}/mappings`);
  }

  getMapping(clientId: string, mappingId: string): Observable<ScimAttributeMapping> {
    return this.api.get<ScimAttributeMapping>(`/scim/clients/${clientId}/mappings/${mappingId}`);
  }

  getMappingOptions(): Observable<ScimMappingOptions> {
    return this.api.get<ScimMappingOptions>('/scim/clients/mapping-options');
  }

  previewMappings(
    clientId: string,
    resource: Record<string, unknown>,
    mode: 'create' | 'replace' | 'patch' = 'create',
  ): Observable<ScimMappingPreview> {
    return this.api.post<ScimMappingPreview>(`/scim/clients/${clientId}/mappings/preview`, {
      resource,
      mode,
    });
  }

  createMapping(
    clientId: string,
    request: SaveScimAttributeMappingRequest,
  ): Observable<ScimAttributeMapping> {
    return this.api.post<ScimAttributeMapping>(`/scim/clients/${clientId}/mappings`, request);
  }

  updateMapping(
    clientId: string,
    mappingId: string,
    request: SaveScimAttributeMappingRequest,
  ): Observable<ScimAttributeMapping> {
    return this.api.put<ScimAttributeMapping>(
      `/scim/clients/${clientId}/mappings/${mappingId}`,
      request,
    );
  }

  deleteMapping(clientId: string, mappingId: string): Observable<void> {
    return this.api.delete<void>(`/scim/clients/${clientId}/mappings/${mappingId}`);
  }

  applyDefaultMappings(clientId: string, provider?: string): Observable<ScimAttributeMapping[]> {
    return this.api.post<ScimAttributeMapping[]>(`/scim/clients/${clientId}/mappings/defaults`, {
      provider,
    });
  }

  // ============================================
  // Outbound providers (we push to them)
  // ============================================

  getProviderOptions(): Observable<ScimProviderOptions> {
    return this.api.get<ScimProviderOptions>('/scim/providers/options');
  }

  getProviders(): Observable<ScimProvider[]> {
    return this.api.get<ScimProvider[]>('/scim/providers');
  }

  getProvider(id: string): Observable<ScimProvider> {
    return this.api.get<ScimProvider>(`/scim/providers/${id}`);
  }

  createProvider(request: CreateScimProviderRequest): Observable<ScimProvider> {
    return this.api.post<ScimProvider>('/scim/providers', request);
  }

  updateProvider(id: string, request: UpdateScimProviderRequest): Observable<ScimProvider> {
    return this.api.put<ScimProvider>(`/scim/providers/${id}`, request);
  }

  deleteProvider(id: string): Observable<void> {
    return this.api.delete<void>(`/scim/providers/${id}`);
  }

  setProviderSecret(id: string, secret: string): Observable<ScimProvider> {
    return this.api.put<ScimProvider>(`/scim/providers/${id}/secret`, { secret });
  }

  testProvider(id: string): Observable<ScimProviderTestResult> {
    return this.api.post<ScimProviderTestResult>(`/scim/providers/${id}/test`, {});
  }

  reconcileProvider(id: string): Observable<ScimProviderReconcileResult> {
    return this.api.post<ScimProviderReconcileResult>(`/scim/providers/${id}/reconcile`, {});
  }

  previewProvider(id: string, userId: string): Observable<ScimProviderPreview> {
    return this.api.post<ScimProviderPreview>(`/scim/providers/${id}/preview`, { userId });
  }

  getProviderQueue(
    id: string,
    status?: string,
    skip = 0,
    take = 50,
  ): Observable<ScimProviderQueueResponse> {
    const params: Record<string, string | number> = { skip, take };
    if (status) params['status'] = status;
    return this.api.get<ScimProviderQueueResponse>(`/scim/providers/${id}/queue`, params);
  }

  retryProviderQueue(id: string): Observable<{ requeued: number }> {
    return this.api.post<{ requeued: number }>(`/scim/providers/${id}/queue/retry`, {});
  }

  /** A provider's calls, newest first; `success` false returns only the failed ones. */
  getProviderLogs(
    id: string,
    page = 1,
    pageSize = 50,
    success?: boolean,
  ): Observable<PaginatedResponse<ScimProviderLog>> {
    return this.api.getPaginated<ScimProviderLog>(
      `/scim/providers/${id}/logs`,
      { page, pageSize },
      success === undefined ? undefined : { success },
    );
  }

  getProviderResources(
    id: string,
    type?: 'User' | 'Group',
    page = 1,
    pageSize = 50,
  ): Observable<PaginatedResponse<ScimProviderResource>> {
    return this.api.getPaginated<ScimProviderResource>(
      `/scim/providers/${id}/resources`,
      { page, pageSize },
      type ? { type } : undefined,
    );
  }

  getProviderMappings(id: string): Observable<ScimProviderAttributeMapping[]> {
    return this.api.get<ScimProviderAttributeMapping[]>(`/scim/providers/${id}/mappings`);
  }

  createProviderMapping(
    id: string,
    request: SaveScimProviderAttributeMappingRequest,
  ): Observable<ScimProviderAttributeMapping> {
    return this.api.post<ScimProviderAttributeMapping>(`/scim/providers/${id}/mappings`, request);
  }

  updateProviderMapping(
    id: string,
    mappingId: string,
    request: SaveScimProviderAttributeMappingRequest,
  ): Observable<ScimProviderAttributeMapping> {
    return this.api.put<ScimProviderAttributeMapping>(
      `/scim/providers/${id}/mappings/${mappingId}`,
      request,
    );
  }

  deleteProviderMapping(id: string, mappingId: string): Observable<void> {
    return this.api.delete<void>(`/scim/providers/${id}/mappings/${mappingId}`);
  }

  applyProviderDefaultMappings(id: string): Observable<ScimProviderAttributeMapping[]> {
    return this.api.post<ScimProviderAttributeMapping[]>(
      `/scim/providers/${id}/mappings/defaults`,
      {},
    );
  }

  // ============================================
  // Suggestions and Options (static data)
  // ============================================

  getScimAttributeSuggestions(): ScimAttributeSuggestion[] {
    return [
      // Core SCIM User attributes
      { attribute: 'userName', description: 'Unique username for the user', provider: 'standard' },
      { attribute: 'name.givenName', description: 'First name', provider: 'standard' },
      { attribute: 'name.familyName', description: 'Last name', provider: 'standard' },
      { attribute: 'name.formatted', description: 'Full formatted name', provider: 'standard' },
      { attribute: 'displayName', description: 'Display name', provider: 'standard' },
      {
        attribute: 'emails[type eq "work"].value',
        description: 'Work email address',
        provider: 'standard',
      },
      {
        attribute: 'emails[primary eq true].value',
        description: 'Primary email address',
        provider: 'standard',
      },
      {
        attribute: 'phoneNumbers[type eq "work"].value',
        description: 'Work phone number',
        provider: 'standard',
      },
      {
        attribute: 'phoneNumbers[type eq "mobile"].value',
        description: 'Mobile phone number',
        provider: 'standard',
      },
      { attribute: 'active', description: 'Whether the user is active', provider: 'standard' },
      { attribute: 'title', description: 'Job title', provider: 'standard' },
      { attribute: 'preferredLanguage', description: 'Preferred language', provider: 'standard' },
      { attribute: 'locale', description: 'Locale', provider: 'standard' },
      { attribute: 'timezone', description: 'Timezone', provider: 'standard' },

      // Enterprise User extension
      {
        attribute: 'urn:ietf:params:scim:schemas:extension:enterprise:2.0:User:employeeNumber',
        description: 'Employee number',
        provider: 'standard',
      },
      {
        attribute: 'urn:ietf:params:scim:schemas:extension:enterprise:2.0:User:department',
        description: 'Department',
        provider: 'standard',
      },
      {
        attribute: 'urn:ietf:params:scim:schemas:extension:enterprise:2.0:User:manager.value',
        description: 'Manager user ID',
        provider: 'standard',
      },
      {
        attribute: 'urn:ietf:params:scim:schemas:extension:enterprise:2.0:User:organization',
        description: 'Organization',
        provider: 'standard',
      },
      {
        attribute: 'urn:ietf:params:scim:schemas:extension:enterprise:2.0:User:costCenter',
        description: 'Cost center',
        provider: 'standard',
      },
      {
        attribute: 'urn:ietf:params:scim:schemas:extension:enterprise:2.0:User:division',
        description: 'Division',
        provider: 'standard',
      },

      // Azure AD specific
      {
        attribute: 'urn:ietf:params:scim:schemas:extension:azure:2.0:User:objectId',
        description: 'Azure AD Object ID',
        provider: 'azure',
      },
      {
        attribute: 'urn:ietf:params:scim:schemas:extension:azure:2.0:User:immutableId',
        description: 'Azure AD Immutable ID',
        provider: 'azure',
      },

      // Okta specific
      {
        attribute: 'urn:ietf:params:scim:schemas:extension:okta:2.0:User:secondEmail',
        description: 'Okta secondary email',
        provider: 'okta',
      },
    ];
  }

  // ============================================
  // Helper to get SCIM endpoint URL
  // ============================================

  /**
   * The tenant-specific SCIM base URL to configure in an upstream provisioning
   * client. Resolved server-side from the tenant's issuer (custom domain /
   * subdomain), not the admin portal's origin.
   */
  getScimEndpointUrl(): Observable<{ url: string }> {
    return this.api.get<{ url: string }>('/scim/clients/endpoint-url');
  }
}

/** Labels for the inbound target properties the server lists in mapping-options. */
const INTERNAL_PROPERTY_LABELS: Record<string, string> = {
  UserName: 'Username',
  Email: 'Email',
  FirstName: 'First name',
  LastName: 'Last name',
  DisplayName: 'Display name',
  PhoneNumber: 'Phone number',
  Locale: 'Locale',
  TimeZone: 'Time zone',
  IsActive: 'Active',
  ProfilePictureUrl: 'Profile picture URL',
};

/** Labels for the transformations the server lists in mapping-options. */
const TRANSFORMATION_LABELS: Record<string, string> = {
  none: 'No transformation',
  lowercase: 'Convert to lowercase',
  uppercase: 'Convert to uppercase',
  trim: 'Trim whitespace',
  normalize_email: 'Normalize email address',
  normalize_phone: 'Normalize phone number',
  extract_domain: 'Extract domain from email',
  format_name: 'Format as proper name (Title Case)',
};

/** The server's inbound target properties, labelled; one without a label shows its name. */
export function internalPropertyOptions(names: readonly string[]): InternalPropertyOption[] {
  return names.map((name) => ({ name, displayName: INTERNAL_PROPERTY_LABELS[name] ?? name }));
}

/** The server's transformations, labelled; one without a label shows its name. */
export function transformationOptions(names: readonly string[]): TransformationOption[] {
  return names.map((name) => ({ name, description: TRANSFORMATION_LABELS[name] ?? name }));
}
