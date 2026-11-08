import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { ApiService } from './api.service';
import {
  UserProfile,
  UpdateProfileRequest,
  SecurityOverview,
  MfaStatus,
  MfaSetupResponse,
  ChangePasswordRequest,
  VerificationStatus,
  ExternalLogin,
  Session,
  ConnectedApp,
  CibaRequest,
} from '../models';

export interface AvatarUploadResult {
  success: boolean;
  url?: string;
}

export interface UploadUrlResult {
  uploadUrl: string;
  fileName: string;
  expiresIn: number;
}

@Injectable({
  providedIn: 'root',
})
export class AccountService {
  private readonly api = inject(ApiService);
  private readonly http = inject(HttpClient);

  // ============================================
  // Profile
  // ============================================

  getProfile(): Observable<UserProfile> {
    return this.api.get<UserProfile>('/profile');
  }

  updateProfile(data: UpdateProfileRequest): Observable<UserProfile> {
    return this.api.put<UserProfile>('/profile', data);
  }

  /**
   * Upload a profile picture directly to the server
   */
  uploadPicture(file: File): Observable<AvatarUploadResult> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<AvatarUploadResult>(`${this.api.baseUrl}/profile/picture`, formData);
  }

  /**
   * Get a signed URL for direct upload to blob storage
   */
  getPictureUploadUrl(contentType: string): Observable<UploadUrlResult> {
    return this.api.post<UploadUrlResult>('/profile/picture/upload-url', { contentType });
  }

  /**
   * Confirm picture upload after direct upload to blob storage
   */
  confirmPictureUpload(fileName: string): Observable<AvatarUploadResult> {
    return this.api.post<AvatarUploadResult>('/profile/picture/confirm', { fileName });
  }

  /**
   * Upload picture directly to blob storage using SAS token
   */
  uploadPictureToBlob(uploadUrl: string, file: File): Observable<void> {
    return this.http.put<void>(uploadUrl, file, {
      headers: {
        'x-ms-blob-type': 'BlockBlob',
        'Content-Type': file.type,
      },
    });
  }

  // ============================================
  // Security Overview
  // ============================================

  getSecurityOverview(): Observable<SecurityOverview> {
    return this.api.get<SecurityOverview>('/security');
  }

  // ============================================
  // Password
  // ============================================

  changePassword(data: ChangePasswordRequest): Observable<void> {
    return this.api.post<void>('/security/password', data);
  }

  // ============================================
  // MFA
  // ============================================

  getMfaStatus(): Observable<MfaStatus> {
    return this.api.get<MfaStatus>('/security/mfa');
  }

  setupMfaMethod(method: 'totp' | 'sms' | 'email'): Observable<MfaSetupResponse> {
    return this.api.post<MfaSetupResponse>(`/security/mfa/${method}/setup`, {});
  }

  verifyMfaMethod(method: 'totp' | 'sms' | 'email', code: string): Observable<void> {
    return this.api.post<void>(`/security/mfa/${method}/verify`, { code });
  }

  disableMfaMethod(method: 'totp' | 'sms' | 'email'): Observable<void> {
    return this.api.delete<void>(`/security/mfa/${method}`);
  }

  // ============================================
  // Email Verification
  // ============================================

  getEmailVerificationStatus(): Observable<VerificationStatus> {
    return this.api.get<VerificationStatus>('/security/email');
  }

  sendEmailVerification(): Observable<void> {
    return this.api.post<void>('/security/email/send', {});
  }

  verifyEmail(code: string): Observable<void> {
    return this.api.post<void>('/security/email/verify', { code });
  }

  updateEmail(email: string): Observable<void> {
    return this.api.put<void>('/security/email', { email });
  }

  // ============================================
  // Phone Verification
  // ============================================

  getPhoneVerificationStatus(): Observable<VerificationStatus> {
    return this.api.get<VerificationStatus>('/security/phone');
  }

  sendPhoneVerification(): Observable<void> {
    return this.api.post<void>('/security/phone/send', {});
  }

  verifyPhone(code: string): Observable<void> {
    return this.api.post<void>('/security/phone/verify', { code });
  }

  updatePhone(phoneNumber: string): Observable<void> {
    return this.api.put<void>('/security/phone', { phoneNumber });
  }

  // ============================================
  // External Logins
  // ============================================

  getExternalLogins(): Observable<ExternalLogin[]> {
    return this.api.get<ExternalLogin[]>('/security/external-logins');
  }

  removeExternalLogin(provider: string, providerKey: string): Observable<void> {
    return this.api.delete<void>(`/security/external-logins/${provider}/${providerKey}`);
  }

  // ============================================
  // Sessions
  // ============================================

  getSessions(): Observable<Session[]> {
    return this.api.get<{ sessions: Session[]; totalCount: number }>('/sessions').pipe(
      map(res => res.sessions ?? [])
    );
  }

  revokeSession(sessionId: string): Observable<void> {
    return this.api.delete<void>(`/sessions/${sessionId}`);
  }

  revokeAllSessions(): Observable<void> {
    return this.api.post<void>('/sessions/revoke-all', {});
  }

  // ============================================
  // Connected Apps
  // ============================================

  getConnectedApps(): Observable<ConnectedApp[]> {
    return this.api.get<{ apps: ConnectedApp[]; totalCount: number }>('/connected-apps').pipe(
      map(res => res.apps ?? [])
    );
  }

  revokeConnectedApp(clientId: string): Observable<void> {
    return this.api.delete<void>(`/connected-apps/${clientId}`);
  }

  // ============================================
  // CIBA Requests
  // ============================================

  getPendingCibaRequests(): Observable<CibaRequest[]> {
    return this.api.get<{ requests: CibaRequest[]; totalCount: number }>('/ciba/pending').pipe(
      map(res => res.requests ?? [])
    );
  }

  approveCibaRequest(authReqId: string): Observable<void> {
    return this.api.post<void>(`/ciba/${authReqId}/approve`, {});
  }

  denyCibaRequest(authReqId: string): Observable<void> {
    return this.api.post<void>(`/ciba/${authReqId}/deny`, {});
  }
}
