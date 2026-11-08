import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  IdentityProvider,
  IdentityProviderEdit,
  CreateIdentityProviderRequest,
  ProviderTypeInfo,
  SchemeAvailability,
  ServerInfo,
  ProviderIconDiscovery,
} from '../models/identity-provider.model';

@Injectable({
  providedIn: 'root',
})
export class IdentityProviderService {
  private readonly api = inject(ApiService);

  private readonly endpoint = '/identity-providers';

  /**
   * Get all identity providers
   */
  getAll(): Observable<IdentityProvider[]> {
    return this.api.get<IdentityProvider[]>(this.endpoint);
  }

  /**
   * Get identity provider for editing (with unmasked secrets)
   */
  getForEdit(id: number): Observable<IdentityProviderEdit> {
    return this.api.get<IdentityProviderEdit>(`${this.endpoint}/${id}/edit`);
  }

  /**
   * Get available provider types
   */
  getProviderTypes(): Observable<ProviderTypeInfo[]> {
    return this.api.get<ProviderTypeInfo[]>(`${this.endpoint}/types`);
  }

  /**
   * Get server info (issuer URI, callback template)
   */
  getServerInfo(): Observable<ServerInfo> {
    return this.api.get<ServerInfo>(`${this.endpoint}/server-info`);
  }

  /**
   * Check if a scheme name is available. Backend route is
   * <c>/check-scheme/{scheme}</c> (path parameter, not query string)
   * — calling the query-string variant would silently 404 and leave
   * the user blocked from choosing any scheme name.
   */
  checkSchemeAvailability(scheme: string): Observable<SchemeAvailability> {
    return this.api.get<SchemeAvailability>(`${this.endpoint}/check-scheme/${encodeURIComponent(scheme)}`);
  }

  /**
   * Create a new identity provider
   */
  create(request: CreateIdentityProviderRequest): Observable<IdentityProvider> {
    return this.api.post<IdentityProvider>(this.endpoint, request);
  }

  /**
   * Update an existing identity provider
   */
  update(id: number, request: CreateIdentityProviderRequest): Observable<IdentityProvider> {
    return this.api.put<IdentityProvider>(`${this.endpoint}/${id}`, request);
  }

  /**
   * Delete an identity provider
   */
  delete(id: number): Observable<void> {
    return this.api.delete<void>(`${this.endpoint}/${id}`);
  }

  /**
   * Toggle identity provider enabled state
   */
  toggle(id: number): Observable<IdentityProvider> {
    return this.api.post<IdentityProvider>(`${this.endpoint}/${id}/toggle`, {});
  }

  /**
   * Test identity provider connection
   */
  test(id: number): Observable<{ success: boolean; message: string }> {
    return this.api.post<{ success: boolean; message: string }>(`${this.endpoint}/${id}/test`, {});
  }

  /**
   * Get a secret value for editing
   */
  /** Looks for icons on the provider's site; nothing is stored until setIcon is called. */
  discoverIcons(id: number): Observable<ProviderIconDiscovery> {
    return this.api.post<ProviderIconDiscovery>(`${this.endpoint}/${id}/icon/discover`, {});
  }

  /** Stores a PNG data URL (normally one returned by discoverIcons) as the provider icon. */
  setIcon(id: number, dataUrl: string): Observable<IdentityProvider> {
    return this.api.put<IdentityProvider>(`${this.endpoint}/${id}/icon`, { dataUrl });
  }

  getSecret(id: number, secretKey: string): Observable<string> {
    return this.api.get<string>(`${this.endpoint}/${id}/secrets/${secretKey}`);
  }
}
