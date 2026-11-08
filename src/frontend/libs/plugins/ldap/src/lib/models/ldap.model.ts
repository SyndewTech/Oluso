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
  adminPassword?: string;
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

export interface ResetPasswordRequest {
  newPassword: string;
}
