import api from './api';

export interface SigningKey {
  id: string;
  name: string;
  keyId: string;
  keyType: string;
  algorithm: string;
  use: string;
  keySize: number;
  clientId?: string;
  status: string;
  createdAt: string;
  activatedAt?: string;
  expiresAt?: string;
  revokedAt?: string;
  revocationReason?: string;
  lastUsedAt?: string;
  signatureCount: number;
  priority: number;
  includeInJwks: boolean;
  canSign: boolean;
  canVerify: boolean;
  isExpiringSoon: boolean;
  isExpired: boolean;
}

export interface GenerateKeyRequest {
  clientId?: string;
  name?: string;
  keyType?: string;
  algorithm?: string;
  keySize?: number;
  lifetimeDays?: number;
  activateImmediately?: boolean;
  priority?: number;
}

export interface UpdateKeyStatusRequest {
  status?: string;
  priority?: number;
  includeInJwks?: boolean;
  expiresAt?: string;
}

export interface KeyRotationConfig {
  clientId?: string;
  enabled?: boolean;
  keyType?: string;
  algorithm?: string;
  keySize?: number;
  keyLifetimeDays?: number;
  rotationLeadDays?: number;
  gracePeriodDays?: number;
  maxKeys?: number;
  lastRotationAt?: string;
  nextRotationAt?: string;
}

const signingKeyService = {
  async getKeys(clientId?: string): Promise<SigningKey[]> {
    const params = clientId ? `?clientId=${clientId}` : '';
    const response = await api.get<SigningKey[]>(`/signing-keys${params}`);
    return response.data;
  },

  async getKey(id: string): Promise<SigningKey> {
    const response = await api.get<SigningKey>(`/signing-keys/${id}`);
    return response.data;
  },

  async generateKey(request: GenerateKeyRequest): Promise<SigningKey> {
    const response = await api.post<SigningKey>('/signing-keys', request);
    return response.data;
  },

  async rotateKeys(clientId?: string): Promise<SigningKey> {
    const params = clientId ? `?clientId=${clientId}` : '';
    const response = await api.post<SigningKey>(`/signing-keys/rotate${params}`);
    return response.data;
  },

  async updateKeyStatus(id: string, request: UpdateKeyStatusRequest): Promise<SigningKey> {
    const response = await api.patch<SigningKey>(`/signing-keys/${id}/status`, request);
    return response.data;
  },

  async revokeKey(id: string, reason?: string): Promise<void> {
    await api.post(`/signing-keys/${id}/revoke`, { reason });
  },

  async deleteKey(id: string): Promise<void> {
    await api.delete(`/signing-keys/${id}`);
  },

  async getRotationConfig(clientId?: string): Promise<KeyRotationConfig> {
    const params = clientId ? `?clientId=${clientId}` : '';
    const response = await api.get<KeyRotationConfig>(`/signing-keys/rotation-config${params}`);
    return response.data;
  },

  async updateRotationConfig(config: KeyRotationConfig): Promise<KeyRotationConfig> {
    const response = await api.put<KeyRotationConfig>('/signing-keys/rotation-config', config);
    return response.data;
  },

  async getExpiringKeys(daysUntilExpiration: number = 14): Promise<SigningKey[]> {
    const response = await api.get<SigningKey[]>(`/signing-keys/expiring?daysUntilExpiration=${daysUntilExpiration}`);
    return response.data;
  }
};

export default signingKeyService;
