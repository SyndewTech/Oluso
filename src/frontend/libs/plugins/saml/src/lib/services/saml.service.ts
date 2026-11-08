import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '@oluso/admin-ui';
import {
  SamlServiceProvider,
  SamlServiceProviderEdit,
  SamlIdpInfo,
  CreateSamlServiceProviderRequest,
  SamlTestResult,
  SamlIdpConfiguration,
  SamlCertificateInfo,
  UploadCertificateRequest,
  EntityIdAvailability,
} from '../models/saml.model';

/**
 * SAML Service
 *
 * Provides HTTP methods for managing SAML Service Providers and IdP configuration.
 */
@Injectable({ providedIn: 'root' })
export class SamlService {
  private readonly api = inject(ApiService);
  private readonly basePath = '/saml/service-providers';

  /**
   * Get IdP info
   */
  getIdpInfo(): Observable<SamlIdpInfo> {
    return this.api.get<SamlIdpInfo>(`${this.basePath}/idp-info`);
  }

  /**
   * Check if an entity ID is available
   */
  checkEntityIdAvailability(entityId: string, excludeId?: number): Observable<EntityIdAvailability> {
    const params: Record<string, string | number | boolean> = {};
    if (excludeId) {
      params['excludeId'] = excludeId;
    }
    return this.api.get<EntityIdAvailability>(
      `${this.basePath}/check-entity-id/${encodeURIComponent(entityId)}`,
      params
    );
  }

  /**
   * Get all service providers
   */
  getAll(includeDisabled = false): Observable<SamlServiceProvider[]> {
    return this.api.get<SamlServiceProvider[]>(this.basePath, { includeDisabled });
  }

  /**
   * Get a service provider by ID
   */
  getById(id: number): Observable<SamlServiceProvider> {
    return this.api.get<SamlServiceProvider>(`${this.basePath}/${id}`);
  }

  /**
   * Get a service provider for editing (includes certificates)
   */
  getForEdit(id: number): Observable<SamlServiceProviderEdit> {
    return this.api.get<SamlServiceProviderEdit>(`${this.basePath}/${id}/edit`);
  }

  /**
   * Create a new service provider
   */
  create(data: CreateSamlServiceProviderRequest): Observable<SamlServiceProvider> {
    return this.api.post<SamlServiceProvider>(this.basePath, data);
  }

  /**
   * Update a service provider
   */
  update(id: number, data: SamlServiceProviderEdit): Observable<SamlServiceProvider> {
    return this.api.put<SamlServiceProvider>(`${this.basePath}/${id}`, data);
  }

  /**
   * Delete a service provider
   */
  delete(id: number): Observable<void> {
    return this.api.delete<void>(`${this.basePath}/${id}`);
  }

  /**
   * Toggle a service provider's enabled status
   */
  toggle(id: number): Observable<SamlServiceProvider> {
    return this.api.post<SamlServiceProvider>(`${this.basePath}/${id}/toggle`, {});
  }

  /**
   * Test a service provider configuration
   */
  test(id: number): Observable<SamlTestResult> {
    return this.api.post<SamlTestResult>(`${this.basePath}/${id}/test`, {});
  }

  // SAML IdP Configuration

  /**
   * Get IdP configuration
   */
  getIdpConfiguration(): Observable<SamlIdpConfiguration> {
    return this.api.get<SamlIdpConfiguration>(`${this.basePath}/idp-configuration`);
  }

  /**
   * Update IdP configuration
   */
  updateIdpConfiguration(data: SamlIdpConfiguration): Observable<SamlIdpConfiguration> {
    return this.api.put<SamlIdpConfiguration>(`${this.basePath}/idp-configuration`, data);
  }

  // IdP Certificate Management

  /**
   * Get signing certificate info
   */
  getSigningCertificate(): Observable<SamlCertificateInfo> {
    return this.api.get<SamlCertificateInfo>(`${this.basePath}/idp-certificate/signing`);
  }

  /**
   * Get encryption certificate info
   */
  getEncryptionCertificate(): Observable<SamlCertificateInfo> {
    return this.api.get<SamlCertificateInfo>(`${this.basePath}/idp-certificate/encryption`);
  }

  /**
   * Generate a new signing certificate
   */
  generateSigningCertificate(): Observable<SamlCertificateInfo> {
    return this.api.post<SamlCertificateInfo>(`${this.basePath}/idp-certificate/signing/generate`, {});
  }

  /**
   * Generate a new encryption certificate
   */
  generateEncryptionCertificate(): Observable<SamlCertificateInfo> {
    return this.api.post<SamlCertificateInfo>(`${this.basePath}/idp-certificate/encryption/generate`, {});
  }

  /**
   * Upload a signing certificate
   */
  uploadSigningCertificate(data: UploadCertificateRequest): Observable<SamlCertificateInfo> {
    return this.api.post<SamlCertificateInfo>(`${this.basePath}/idp-certificate/signing/upload`, data);
  }

  /**
   * Upload an encryption certificate
   */
  uploadEncryptionCertificate(data: UploadCertificateRequest): Observable<SamlCertificateInfo> {
    return this.api.post<SamlCertificateInfo>(`${this.basePath}/idp-certificate/encryption/upload`, data);
  }

  /**
   * Reset signing certificate to global
   */
  resetSigningCertificate(): Observable<void> {
    return this.api.post<void>(`${this.basePath}/idp-certificate/signing/reset`, {});
  }

  /**
   * Reset encryption certificate to global
   */
  resetEncryptionCertificate(): Observable<void> {
    return this.api.post<void>(`${this.basePath}/idp-certificate/encryption/reset`, {});
  }
}
