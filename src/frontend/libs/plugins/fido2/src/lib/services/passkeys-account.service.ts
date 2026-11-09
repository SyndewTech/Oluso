import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

// Models for account passkeys
export interface AccountPasskey {
  id: string;
  credentialId: string;
  name: string;
  authenticatorType?: string;
  createdAt: string;
  lastUsedAt?: string;
  isResidentKey: boolean;
}

export interface AccountPasskeyList {
  passkeys: AccountPasskey[];
  isEnabled: boolean;
  message?: string;
}

export interface WebAuthnRegistrationOptions {
  challenge: string;
  rp: { id: string; name: string };
  user: { id: string; name: string; displayName: string };
  pubKeyCredParams?: { type: string; alg: number }[];
  timeout?: number;
  attestation?: string;
  authenticatorSelection?: {
    authenticatorAttachment?: string;
    residentKey?: string;
    userVerification?: string;
  };
  excludeCredentials?: { type: string; id: string; transports?: string[] }[];
}

export interface RegistrationStartResponse {
  registrationId: string;
  options: WebAuthnRegistrationOptions;
}

export interface RegistrationCompleteResponse {
  success: boolean;
  credentialId?: string;
  message?: string;
}

/**
 * Service for account passkeys API calls.
 * This service uses the /api/account/passkeys endpoints for end-user self-service.
 */
@Injectable({
  providedIn: 'root',
})
export class PasskeysAccountService {
  private readonly http = inject(HttpClient);
  private baseUrl = '';

  /**
   * Set the base URL for API calls
   */
  setBaseUrl(url: string): void {
    this.baseUrl = url;
  }

  /**
   * Get user's passkeys
   */
  getPasskeys(): Observable<AccountPasskeyList> {
    return this.http.get<AccountPasskeyList>(`${this.baseUrl}/api/account/passkeys`);
  }

  /**
   * Start passkey registration
   */
  startRegistration(options?: {
    authenticatorType?: string;
    requireDiscoverableCredential?: boolean;
  }): Observable<RegistrationStartResponse> {
    return this.http.post<RegistrationStartResponse>(
      `${this.baseUrl}/api/account/passkeys/register/start`,
      options ?? { requireDiscoverableCredential: true }
    );
  }

  /**
   * Complete passkey registration
   */
  completeRegistration(
    registrationId: string,
    attestationResponse: string,
    name?: string
  ): Observable<RegistrationCompleteResponse> {
    return this.http.post<RegistrationCompleteResponse>(
      `${this.baseUrl}/api/account/passkeys/register/complete`,
      {
        registrationId,
        attestationResponse,
        name,
      }
    );
  }

  /**
   * Update passkey name
   */
  updatePasskey(passkeyId: string, name: string): Observable<void> {
    return this.http.patch<void>(`${this.baseUrl}/api/account/passkeys/${passkeyId}`, { name });
  }

  /**
   * Delete a passkey
   */
  deletePasskey(passkeyId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/api/account/passkeys/${passkeyId}`);
  }
}

// WebAuthn helper functions
export function base64UrlToArrayBuffer(base64url: string): ArrayBuffer {
  const base64 = base64url.replace(/-/g, '+').replace(/_/g, '/');
  const padding = '='.repeat((4 - (base64.length % 4)) % 4);
  const binary = atob(base64 + padding);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }
  return bytes.buffer;
}

export function arrayBufferToBase64Url(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  let binary = '';
  for (let i = 0; i < bytes.length; i++) {
    binary += String.fromCharCode(bytes[i]);
  }
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=/g, '');
}

export function getDeviceName(): string {
  const ua = navigator.userAgent;
  if (/iPhone|iPad|iPod/.test(ua)) return 'iPhone/iPad';
  if (/Android/.test(ua)) return 'Android device';
  if (/Mac/.test(ua)) return 'Mac';
  if (/Windows/.test(ua)) return 'Windows PC';
  if (/Linux/.test(ua)) return 'Linux device';
  return 'Unknown device';
}

export function convertOptionsForWebAuthn(
  options: WebAuthnRegistrationOptions
): PublicKeyCredentialCreationOptions {
  return {
    challenge: base64UrlToArrayBuffer(options.challenge),
    rp: options.rp,
    user: {
      ...options.user,
      id: base64UrlToArrayBuffer(options.user.id),
    },
    pubKeyCredParams:
      options.pubKeyCredParams?.map((p) => ({
        type: p.type as PublicKeyCredentialType,
        alg: p.alg,
      })) || [
        { type: 'public-key', alg: -7 }, // ES256
        { type: 'public-key', alg: -257 }, // RS256
      ],
    timeout: options.timeout || 60000,
    attestation: (options.attestation as AttestationConveyancePreference) || 'none',
    authenticatorSelection: options.authenticatorSelection as AuthenticatorSelectionCriteria,
    excludeCredentials: options.excludeCredentials?.map((cred) => ({
      type: cred.type as PublicKeyCredentialType,
      id: base64UrlToArrayBuffer(cred.id),
      transports: cred.transports as AuthenticatorTransport[],
    })),
  };
}
