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

export const KEY_TYPES = ['RSA', 'EC', 'Symmetric'] as const;
export type KeyType = (typeof KEY_TYPES)[number];

export const ALGORITHM_OPTIONS: Record<KeyType, string[]> = {
  RSA: ['RS256', 'RS384', 'RS512', 'PS256', 'PS384', 'PS512'],
  EC: ['ES256', 'ES384', 'ES512'],
  Symmetric: ['HS256', 'HS384', 'HS512'],
};

export const KEY_SIZE_OPTIONS: Record<KeyType, number[]> = {
  RSA: [2048, 3072, 4096],
  EC: [256, 384, 521],
  Symmetric: [256, 384, 512],
};
