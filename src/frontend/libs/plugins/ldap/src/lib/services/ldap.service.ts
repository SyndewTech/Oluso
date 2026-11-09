import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '@syndew/oluso-admin-ui';
import {
  LdapServerInfo,
  LdapServerSettings,
  TestConnectionResponse,
  LdapCertificateInfo,
  UploadCertificateRequest,
  ServiceAccount,
  CreateServiceAccountRequest,
  ResetPasswordRequest,
} from '../models/ldap.model';

/**
 * LDAP Service
 *
 * Provides HTTP methods for managing LDAP Server configuration and service accounts.
 */
@Injectable({ providedIn: 'root' })
export class LdapService {
  private readonly api = inject(ApiService);
  private readonly basePath = '/ldap';

  // Server Settings

  /**
   * Get LDAP server info
   */
  getServerInfo(): Observable<LdapServerInfo> {
    return this.api.get<LdapServerInfo>(`${this.basePath}/server-info`);
  }

  /**
   * Get LDAP settings for current tenant
   */
  getSettings(): Observable<LdapServerSettings> {
    return this.api.get<LdapServerSettings>(`${this.basePath}/settings`);
  }

  /**
   * Update LDAP server settings
   */
  updateSettings(settings: LdapServerSettings): Observable<LdapServerSettings> {
    return this.api.put<LdapServerSettings>(`${this.basePath}/settings`, settings);
  }

  /**
   * Test LDAP server connection
   */
  testConnection(): Observable<TestConnectionResponse> {
    return this.api.post<TestConnectionResponse>(`${this.basePath}/test-connection`, {});
  }

  // Certificate Management

  /**
   * Get TLS certificate info
   */
  getTlsCertificate(): Observable<LdapCertificateInfo> {
    return this.api.get<LdapCertificateInfo>(`${this.basePath}/certificate`);
  }

  /**
   * Generate a new TLS certificate
   */
  generateTlsCertificate(): Observable<LdapCertificateInfo> {
    return this.api.post<LdapCertificateInfo>(`${this.basePath}/certificate/generate`, {});
  }

  /**
   * Upload a TLS certificate (PFX format)
   */
  uploadTlsCertificate(request: UploadCertificateRequest): Observable<LdapCertificateInfo> {
    return this.api.post<LdapCertificateInfo>(`${this.basePath}/certificate/upload`, request);
  }

  /**
   * Reset to global TLS certificate
   */
  resetTlsCertificate(): Observable<void> {
    return this.api.post<void>(`${this.basePath}/certificate/reset`, {});
  }

  // Service Account Management

  /**
   * Get all service accounts
   */
  getServiceAccounts(): Observable<ServiceAccount[]> {
    return this.api.get<ServiceAccount[]>(`${this.basePath}/service-accounts`);
  }

  /**
   * Get a service account by ID
   */
  getServiceAccount(id: string): Observable<ServiceAccount> {
    return this.api.get<ServiceAccount>(`${this.basePath}/service-accounts/${id}`);
  }

  /**
   * Create a new service account
   */
  createServiceAccount(request: CreateServiceAccountRequest): Observable<ServiceAccount> {
    return this.api.post<ServiceAccount>(`${this.basePath}/service-accounts`, request);
  }

  /**
   * Update a service account
   */
  updateServiceAccount(id: string, request: ServiceAccount): Observable<ServiceAccount> {
    return this.api.put<ServiceAccount>(`${this.basePath}/service-accounts/${id}`, request);
  }

  /**
   * Reset service account password
   */
  resetServiceAccountPassword(id: string, request: ResetPasswordRequest): Observable<void> {
    return this.api.post<void>(`${this.basePath}/service-accounts/${id}/reset-password`, request);
  }

  /**
   * Delete a service account
   */
  deleteServiceAccount(id: string): Observable<void> {
    return this.api.delete<void>(`${this.basePath}/service-accounts/${id}`);
  }
}
