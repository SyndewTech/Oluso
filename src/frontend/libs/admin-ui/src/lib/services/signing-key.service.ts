import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  SigningKey,
  GenerateKeyRequest,
  UpdateKeyStatusRequest,
  KeyRotationConfig,
} from '../models/signing-key.model';

@Injectable({
  providedIn: 'root',
})
export class SigningKeyService {
  private readonly api = inject(ApiService);

  getKeys(clientId?: string): Observable<SigningKey[]> {
    const params = clientId ? { clientId } : undefined;
    return this.api.get<SigningKey[]>('/signing-keys', params);
  }

  getKey(id: string): Observable<SigningKey> {
    return this.api.get<SigningKey>(`/signing-keys/${id}`);
  }

  generateKey(request: GenerateKeyRequest): Observable<SigningKey> {
    return this.api.post<SigningKey>('/signing-keys', request);
  }

  rotateKeys(clientId?: string): Observable<SigningKey> {
    const params = clientId ? { clientId } : {};
    return this.api.post<SigningKey>('/signing-keys/rotate', params);
  }

  updateKeyStatus(id: string, request: UpdateKeyStatusRequest): Observable<SigningKey> {
    return this.api.patch<SigningKey>(`/signing-keys/${id}/status`, request);
  }

  revokeKey(id: string, reason?: string): Observable<void> {
    return this.api.post<void>(`/signing-keys/${id}/revoke`, { reason });
  }

  deleteKey(id: string): Observable<void> {
    return this.api.delete<void>(`/signing-keys/${id}`);
  }

  getRotationConfig(clientId?: string): Observable<KeyRotationConfig> {
    const params = clientId ? { clientId } : undefined;
    return this.api.get<KeyRotationConfig>('/signing-keys/rotation-config', params);
  }

  updateRotationConfig(config: KeyRotationConfig): Observable<KeyRotationConfig> {
    return this.api.put<KeyRotationConfig>('/signing-keys/rotation-config', config);
  }

  getExpiringKeys(daysUntilExpiration = 14): Observable<SigningKey[]> {
    return this.api.get<SigningKey[]>('/signing-keys/expiring', { daysUntilExpiration });
  }
}
