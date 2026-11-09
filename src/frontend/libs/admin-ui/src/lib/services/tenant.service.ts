import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  Tenant,
  PasswordPolicy,
  CreateTenantRequest,
  UpdateTenantRequest,
  UpdatePasswordPolicyRequest,
  ProtocolConfiguration,
  UpdateProtocolConfigurationRequest,
  TenantFeaturesResponse,
  SetTenantFeaturesRequest,
} from '../models/tenant.model';
import { JourneyPolicyListItem } from '../models/journey.model';
import { IdentityProvider } from '../models/identity-provider.model';
import { PaginationParams } from '../models/pagination-params.model';
import { PaginatedResponse, LocaleSettings } from '@syndew/oluso-ui-core';

export interface CustomWorkspaceApp {
  id: string;
  name: string;
  description?: string;
  externalUrl: string;
  icon: string;
  color: string;
  order: number;
}

export interface WorkspaceAppsSettings {
  builtInApps: Record<string, boolean>;
  customApps: CustomWorkspaceApp[];
  /** Tenant URL overrides for the built-in Admin and Account apps, keyed "admin" / "account". */
  appUrls?: Record<string, string>;
}

export interface RegisteredModuleInfo {
  featureKey: string;
  displayName: string;
  description: string;
  version?: string;
  category?: string;
  iconName?: string;
  color?: string;
}

@Injectable({
  providedIn: 'root',
})
export class TenantService {
  private readonly api = inject(ApiService);

  getAll(): Observable<Tenant[]> {
    return this.api.get<Tenant[]>('/tenants');
  }

  getMyOrganizationTenants(
    pagination: PaginationParams,
    filters?: { search?: string }
  ): Observable<PaginatedResponse<Tenant>> {
    return this.api.getPaginated<Tenant>(
      '/tenants/my-organization',
      pagination,
      filters as Record<string, string | number | boolean>
    );
  }

  getById(tenantId: string): Observable<Tenant> {
    return this.api.get<Tenant>(`/tenants/${tenantId}`);
  }

  create(data: CreateTenantRequest): Observable<Tenant> {
    return this.api.post<Tenant>('/tenants', data);
  }

  update(tenantId: string, data: UpdateTenantRequest): Observable<Tenant> {
    return this.api.put<Tenant>(`/tenants/${tenantId}`, data);
  }

  delete(tenantId: string): Observable<void> {
    return this.api.delete<void>(`/tenants/${tenantId}`);
  }

  // Password Policy
  getPasswordPolicy(tenantId: string): Observable<PasswordPolicy> {
    return this.api.get<PasswordPolicy>(`/tenants/${tenantId}/password-policy`);
  }

  updatePasswordPolicy(tenantId: string, data: UpdatePasswordPolicyRequest): Observable<PasswordPolicy> {
    return this.api.put<PasswordPolicy>(`/tenants/${tenantId}/password-policy`, data);
  }

  resetPasswordPolicy(tenantId: string): Observable<void> {
    return this.api.delete<void>(`/tenants/${tenantId}/password-policy`);
  }

  // Protocol Configuration
  getProtocolConfiguration(tenantId: string): Observable<ProtocolConfiguration> {
    return this.api.get<ProtocolConfiguration>(`/tenants/${tenantId}/protocol-configuration`);
  }

  updateProtocolConfiguration(tenantId: string, data: UpdateProtocolConfigurationRequest): Observable<ProtocolConfiguration> {
    return this.api.put<ProtocolConfiguration>(`/tenants/${tenantId}/protocol-configuration`, data);
  }

  resetProtocolConfiguration(tenantId: string): Observable<void> {
    return this.api.delete<void>(`/tenants/${tenantId}/protocol-configuration`);
  }

  // Locale Settings
  getLocaleSettings(tenantId: string): Observable<LocaleSettings> {
    return this.api.get<LocaleSettings>(`/tenants/${tenantId}/locale`);
  }

  updateLocaleSettings(tenantId: string, data: LocaleSettings): Observable<LocaleSettings> {
    return this.api.put<LocaleSettings>(`/tenants/${tenantId}/locale`, data);
  }

  // Workspace Apps Settings
  // Pick-lists for a specific tenant's settings page: the tenant in the route,
  // not the one active in the switcher. Server-side access check per tenant.
  getIdentityProviders(tenantId: string): Observable<IdentityProvider[]> {
    return this.api.get<IdentityProvider[]>(`/tenants/${tenantId}/identity-providers`);
  }

  getJourneyPolicies(tenantId: string, includeGlobal = true): Observable<JourneyPolicyListItem[]> {
    return this.api.get<JourneyPolicyListItem[]>(`/tenants/${tenantId}/journeys`, { includeGlobal });
  }

  getWorkspaceAppsSettings(tenantId: string): Observable<WorkspaceAppsSettings> {
    return this.api.get<WorkspaceAppsSettings>(`/tenants/${tenantId}/workspace-apps`);
  }

  // Registered modules (drives the workspace-apps built-in list — single
  // source of truth comes from each module's IModuleDescriptor on the backend)
  getRegisteredModules(): Observable<{ modules: RegisteredModuleInfo[] }> {
    return this.api.get<{ modules: RegisteredModuleInfo[] }>(`/features/modules`);
  }

  updateWorkspaceAppsSettings(tenantId: string, data: WorkspaceAppsSettings): Observable<WorkspaceAppsSettings> {
    return this.api.put<WorkspaceAppsSettings>(`/tenants/${tenantId}/workspace-apps`, data);
  }

  // Feature Entitlements
  getFeatures(tenantId: string): Observable<TenantFeaturesResponse> {
    return this.api.get<TenantFeaturesResponse>(`/tenants/${tenantId}/features`);
  }

  setFeatures(tenantId: string, request: SetTenantFeaturesRequest): Observable<void> {
    return this.api.put<void>(`/tenants/${tenantId}/features`, request);
  }

  enableFeature(tenantId: string, featureKey: string): Observable<void> {
    return this.api.post<void>(`/tenants/${tenantId}/features/${featureKey}`, {});
  }

  disableFeature(tenantId: string, featureKey: string): Observable<void> {
    return this.api.delete<void>(`/tenants/${tenantId}/features/${featureKey}`);
  }
}
