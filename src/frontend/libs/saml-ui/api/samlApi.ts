import type { AxiosInstance } from 'axios';

export interface SamlServiceProvider {
  id: number;
  entityId: string;
  displayName?: string;
  description?: string;
  enabled: boolean;
  metadataUrl?: string;
  assertionConsumerServiceUrl?: string;
  singleLogoutServiceUrl?: string;
  hasSigningCertificate: boolean;
  hasEncryptionCertificate: boolean;
  encryptAssertions: boolean;
  nameIdFormat?: string;
  allowedClaims?: string[];
  claimMappings?: Record<string, string>;
  ssoBinding: string;
  signResponses: boolean;
  signAssertions: boolean;
  requireSignedAuthnRequests: boolean;
  defaultRelayState?: string;
  nonEditable: boolean;
  created: string;
  updated?: string;
  lastAccessed?: string;
}

export interface SamlServiceProviderEdit {
  id: number;
  entityId: string;
  displayName?: string;
  description?: string;
  enabled: boolean;
  metadataUrl?: string;
  assertionConsumerServiceUrl?: string;
  singleLogoutServiceUrl?: string;
  signingCertificate?: string;
  encryptionCertificate?: string;
  encryptAssertions: boolean;
  nameIdFormat?: string;
  allowedClaims?: string[];
  claimMappings?: Record<string, string>;
  ssoBinding: string;
  signResponses: boolean;
  signAssertions: boolean;
  requireSignedAuthnRequests: boolean;
  defaultRelayState?: string;
  nonEditable: boolean;
}

export interface IdpInfo {
  enabled: boolean;
  entityId: string;
  singleSignOnServiceUrl: string;
  singleLogoutServiceUrl: string;
  metadataUrl: string;
  supportedNameIdFormats: string[];
}

export interface CreateSamlServiceProviderRequest {
  entityId: string;
  displayName?: string;
  description?: string;
  enabled?: boolean;
  metadataUrl?: string;
  assertionConsumerServiceUrl?: string;
  singleLogoutServiceUrl?: string;
  signingCertificate?: string;
  encryptionCertificate?: string;
  encryptAssertions?: boolean;
  nameIdFormat?: string;
  allowedClaims?: string[];
  claimMappings?: Record<string, string>;
  ssoBinding?: string;
  signResponses?: boolean;
  signAssertions?: boolean;
  requireSignedAuthnRequests?: boolean;
  defaultRelayState?: string;
}

export interface UpdateSamlServiceProviderRequest {
  displayName?: string;
  description?: string;
  enabled?: boolean;
  metadataUrl?: string;
  assertionConsumerServiceUrl?: string;
  singleLogoutServiceUrl?: string;
  signingCertificate?: string;
  encryptionCertificate?: string;
  encryptAssertions?: boolean;
  nameIdFormat?: string;
  allowedClaims?: string[];
  claimMappings?: Record<string, string>;
  ssoBinding?: string;
  signResponses?: boolean;
  signAssertions?: boolean;
  requireSignedAuthnRequests?: boolean;
  defaultRelayState?: string;
}

export interface TestResult {
  success: boolean;
  message: string;
}

export interface SamlIdpConfiguration {
  enabled: boolean;
  loginJourneyName?: string;
}

export interface UpdateSamlIdpConfigurationRequest {
  enabled?: boolean;
  loginJourneyName?: string;
}

export interface SamlCertificateInfo {
  source: 'Global' | 'Auto' | 'Uploaded';
  certificateId?: string;
  subject?: string;
  issuer?: string;
  notBefore?: string;
  notAfter?: string;
  thumbprint?: string;
  isExpired: boolean;
  isExpiringSoon: boolean;
  hasCertificate: boolean;
}

export interface UploadCertificateRequest {
  base64Pfx: string;
  password?: string;
}

let apiClientInstance: AxiosInstance | null = null;

export function setApiClient(client: AxiosInstance) {
  apiClientInstance = client;
}

function getApiClient(): AxiosInstance {
  if (!apiClientInstance) {
    throw new Error('API client not initialized. Call setApiClient first.');
  }
  return apiClientInstance;
}

const BASE_PATH = '/saml/service-providers';

export const samlApi = {
  async getIdpInfo(): Promise<IdpInfo> {
    const response = await getApiClient().get<IdpInfo>(`${BASE_PATH}/idp-info`);
    return response.data;
  },

  async checkEntityIdAvailability(
    entityId: string,
    excludeId?: number
  ): Promise<{ available: boolean; message?: string }> {
    const params = excludeId ? { excludeId } : undefined;
    const response = await getApiClient().get<{ available: boolean; message?: string }>(
      `${BASE_PATH}/check-entity-id/${encodeURIComponent(entityId)}`,
      { params }
    );
    return response.data;
  },

  async getAll(includeDisabled = false): Promise<SamlServiceProvider[]> {
    const response = await getApiClient().get<SamlServiceProvider[]>(
      BASE_PATH,
      { params: { includeDisabled } }
    );
    return response.data;
  },

  async getById(id: number): Promise<SamlServiceProvider> {
    const response = await getApiClient().get<SamlServiceProvider>(
      `${BASE_PATH}/${id}`
    );
    return response.data;
  },

  async getForEdit(id: number): Promise<SamlServiceProviderEdit> {
    const response = await getApiClient().get<SamlServiceProviderEdit>(
      `${BASE_PATH}/${id}/edit`
    );
    return response.data;
  },

  async create(data: CreateSamlServiceProviderRequest): Promise<SamlServiceProvider> {
    const response = await getApiClient().post<SamlServiceProvider>(
      BASE_PATH,
      data
    );
    return response.data;
  },

  async update(id: number, data: UpdateSamlServiceProviderRequest): Promise<SamlServiceProvider> {
    const response = await getApiClient().put<SamlServiceProvider>(
      `${BASE_PATH}/${id}`,
      data
    );
    return response.data;
  },

  async delete(id: number): Promise<void> {
    await getApiClient().delete(`${BASE_PATH}/${id}`);
  },

  async toggle(id: number): Promise<SamlServiceProvider> {
    const response = await getApiClient().post<SamlServiceProvider>(
      `${BASE_PATH}/${id}/toggle`
    );
    return response.data;
  },

  async test(id: number): Promise<TestResult> {
    const response = await getApiClient().post<TestResult>(
      `${BASE_PATH}/${id}/test`
    );
    return response.data;
  },

  // SAML IdP Configuration (uses current tenant context from X-Tenant-Id header)
  async getIdpConfiguration(): Promise<SamlIdpConfiguration> {
    const response = await getApiClient().get<SamlIdpConfiguration>(
      `${BASE_PATH}/idp-configuration`
    );
    return response.data;
  },

  async updateIdpConfiguration(
    data: UpdateSamlIdpConfigurationRequest
  ): Promise<SamlIdpConfiguration> {
    const response = await getApiClient().put<SamlIdpConfiguration>(
      `${BASE_PATH}/idp-configuration`,
      data
    );
    return response.data;
  },

  // IdP Certificate Management
  async getSigningCertificate(): Promise<SamlCertificateInfo> {
    const response = await getApiClient().get<SamlCertificateInfo>(
      `${BASE_PATH}/idp-certificate/signing`
    );
    return response.data;
  },

  async getEncryptionCertificate(): Promise<SamlCertificateInfo> {
    const response = await getApiClient().get<SamlCertificateInfo>(
      `${BASE_PATH}/idp-certificate/encryption`
    );
    return response.data;
  },

  async generateSigningCertificate(): Promise<SamlCertificateInfo> {
    const response = await getApiClient().post<SamlCertificateInfo>(
      `${BASE_PATH}/idp-certificate/signing/generate`
    );
    return response.data;
  },

  async generateEncryptionCertificate(): Promise<SamlCertificateInfo> {
    const response = await getApiClient().post<SamlCertificateInfo>(
      `${BASE_PATH}/idp-certificate/encryption/generate`
    );
    return response.data;
  },

  async uploadSigningCertificate(data: UploadCertificateRequest): Promise<SamlCertificateInfo> {
    const response = await getApiClient().post<SamlCertificateInfo>(
      `${BASE_PATH}/idp-certificate/signing/upload`,
      data
    );
    return response.data;
  },

  async uploadEncryptionCertificate(data: UploadCertificateRequest): Promise<SamlCertificateInfo> {
    const response = await getApiClient().post<SamlCertificateInfo>(
      `${BASE_PATH}/idp-certificate/encryption/upload`,
      data
    );
    return response.data;
  },

  async resetSigningCertificate(): Promise<void> {
    await getApiClient().post(`${BASE_PATH}/idp-certificate/signing/reset`);
  },

  async resetEncryptionCertificate(): Promise<void> {
    await getApiClient().post(`${BASE_PATH}/idp-certificate/encryption/reset`);
  },
};

export default samlApi;
