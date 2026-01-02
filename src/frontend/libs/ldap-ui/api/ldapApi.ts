import type { AxiosInstance } from 'axios';

let apiClient: AxiosInstance;

export function setApiClient(client: AxiosInstance) {
  apiClient = client;
}

export interface LdapServerInfo {
  enabled: boolean;
  port: number;
  sslPort: number;
  enableSsl: boolean;
  enableStartTls: boolean;
  baseDn: string;
  organization: string;
  userOu: string;
  groupOu: string;
  allowAnonymousBind: boolean;
  maxSearchResults: number;
  tenantIsolation: boolean;
  adminDn: string;
}

export interface LdapServerSettings {
  enabled: boolean;
  baseDn?: string;
  organization?: string;
  allowAnonymousBind: boolean;
  maxSearchResults?: number;
  adminDn?: string;
}

export interface UpdateLdapServerSettingsRequest {
  enabled: boolean;
  baseDn?: string;
  organization?: string;
  allowAnonymousBind: boolean;
  maxSearchResults?: number;
  adminDn?: string;
}

export interface TestConnectionResponse {
  success: boolean;
  message: string;
  details?: Record<string, unknown>;
}

export interface LdapCertificateInfo {
  hasCertificate: boolean;
  source: 'Global' | 'Auto' | 'Uploaded' | 'NotConfigured';
  subject?: string;
  thumbprint?: string;
  notBefore?: string;
  notAfter?: string;
  isExpired: boolean;
  isExpiringSoon: boolean;
}

export interface UploadCertificateRequest {
  base64Pfx: string;
  password?: string;
}

// Service Account types
export type ServiceAccountPermission = 'ReadOnly' | 'SearchOnly' | 'FullRead';

export interface ServiceAccount {
  id: string;
  name: string;
  description?: string;
  bindDn: string;
  isEnabled: boolean;
  permission: ServiceAccountPermission;
  allowedOus: string[];
  allowedIpRanges: string[];
  maxSearchResults?: number;
  rateLimitPerMinute?: number;
  createdAt: string;
  updatedAt?: string;
  lastUsedAt?: string;
  expiresAt?: string;
  isExpired: boolean;
}

export interface CreateServiceAccountRequest {
  name: string;
  description?: string;
  password: string;
  permission: ServiceAccountPermission;
  allowedOus?: string[];
  allowedIpRanges?: string[];
  maxSearchResults?: number;
  rateLimitPerMinute?: number;
  expiresAt?: string;
}

export interface UpdateServiceAccountRequest {
  name?: string;
  description?: string;
  isEnabled?: boolean;
  permission?: ServiceAccountPermission;
  allowedOus?: string[];
  allowedIpRanges?: string[];
  maxSearchResults?: number;
  rateLimitPerMinute?: number;
  expiresAt?: string;
}

export interface ResetPasswordRequest {
  newPassword: string;
}

export const ldapApi = {
  getServerInfo: async (): Promise<LdapServerInfo> => {
    const response = await apiClient.get<LdapServerInfo>('/ldap/server-info');
    return response.data;
  },

  getSettings: async (): Promise<LdapServerSettings> => {
    const response = await apiClient.get<LdapServerSettings>('/ldap/settings');
    return response.data;
  },

  updateSettings: async (settings: UpdateLdapServerSettingsRequest): Promise<LdapServerSettings> => {
    const response = await apiClient.put<LdapServerSettings>('/ldap/settings', settings);
    return response.data;
  },

  testConnection: async (): Promise<TestConnectionResponse> => {
    const response = await apiClient.post<TestConnectionResponse>('/ldap/test-connection');
    return response.data;
  },

  // Certificate management
  getTlsCertificate: async (): Promise<LdapCertificateInfo> => {
    const response = await apiClient.get<LdapCertificateInfo>('/ldap/certificate');
    return response.data;
  },

  generateTlsCertificate: async (): Promise<LdapCertificateInfo> => {
    const response = await apiClient.post<LdapCertificateInfo>('/ldap/certificate/generate');
    return response.data;
  },

  uploadTlsCertificate: async (request: UploadCertificateRequest): Promise<LdapCertificateInfo> => {
    const response = await apiClient.post<LdapCertificateInfo>('/ldap/certificate/upload', request);
    return response.data;
  },

  resetTlsCertificate: async (): Promise<void> => {
    await apiClient.post('/ldap/certificate/reset');
  },

  // Service Account management
  getServiceAccounts: async (): Promise<ServiceAccount[]> => {
    const response = await apiClient.get<ServiceAccount[]>('/ldap/service-accounts');
    return response.data;
  },

  getServiceAccount: async (id: string): Promise<ServiceAccount> => {
    const response = await apiClient.get<ServiceAccount>(`/ldap/service-accounts/${id}`);
    return response.data;
  },

  createServiceAccount: async (request: CreateServiceAccountRequest): Promise<ServiceAccount> => {
    const response = await apiClient.post<ServiceAccount>('/ldap/service-accounts', request);
    return response.data;
  },

  updateServiceAccount: async (id: string, request: UpdateServiceAccountRequest): Promise<ServiceAccount> => {
    const response = await apiClient.put<ServiceAccount>(`/ldap/service-accounts/${id}`, request);
    return response.data;
  },

  resetServiceAccountPassword: async (id: string, request: ResetPasswordRequest): Promise<void> => {
    await apiClient.post(`/ldap/service-accounts/${id}/reset-password`, request);
  },

  deleteServiceAccount: async (id: string): Promise<void> => {
    await apiClient.delete(`/ldap/service-accounts/${id}`);
  },
};
