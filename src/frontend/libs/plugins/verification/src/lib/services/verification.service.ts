import { Injectable, inject } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiService } from '@oluso/admin-ui';
import {
  BusinessApplicant,
  BusinessReviewQueue,
  BusinessVerificationCountry,
  IdentityVerificationSettings,
  ReviewDecision,
  UpdateIdentityVerificationSettingsRequest,
  VerificationConnectionTestResult,
  VerificationProviderInfo,
  VerifiedBusinessDetail,
  VerifiedBusinessList,
} from '../models/verification.model';

/** Business (KYB) verification: providers, settings, businesses, applicants and reviews (api/admin/business-verification). */
@Injectable({ providedIn: 'root' })
export class VerificationService {
  private readonly api = inject(ApiService);
  private readonly endpoint = '/business-verification';

  getProviders(): Observable<VerificationProviderInfo[]> {
    return this.api.get<VerificationProviderInfo[]>(`${this.endpoint}/providers`);
  }

  getCountries(): Observable<BusinessVerificationCountry[]> {
    return this.api.get<BusinessVerificationCountry[]>(`${this.endpoint}/countries`);
  }

  getSettings(): Observable<IdentityVerificationSettings> {
    return this.api.get<IdentityVerificationSettings>(`${this.endpoint}/settings`);
  }

  updateSettings(input: UpdateIdentityVerificationSettingsRequest): Observable<IdentityVerificationSettings> {
    return this.api.put<IdentityVerificationSettings>(`${this.endpoint}/settings`, input);
  }

  testProvider(providerId: string): Observable<VerificationConnectionTestResult> {
    return this.api.post<VerificationConnectionTestResult>(`${this.endpoint}/providers/${encodeURIComponent(providerId)}/test`, {});
  }

  listBusinesses(status: string, search: string, page: number, pageSize: number): Observable<VerifiedBusinessList> {
    const params: Record<string, string | number> = { page, pageSize };
    if (status) params['status'] = status;
    if (search) params['search'] = search;
    return this.api.get<VerifiedBusinessList>(`${this.endpoint}/businesses`, params);
  }

  getBusiness(id: string): Observable<VerifiedBusinessDetail> {
    return this.api.get<VerifiedBusinessDetail>(`${this.endpoint}/businesses/${encodeURIComponent(id)}`);
  }

  reviewQueue(page: number, pageSize: number): Observable<BusinessReviewQueue> {
    return this.api.get<BusinessReviewQueue>(`${this.endpoint}/review-queue`, { page, pageSize });
  }

  reviewBusiness(id: string, decision: ReviewDecision, note?: string): Observable<VerifiedBusinessDetail> {
    return this.api.post<VerifiedBusinessDetail>(`${this.endpoint}/businesses/${encodeURIComponent(id)}/review`, { decision, note });
  }

  reviewApplicant(id: string, decision: ReviewDecision, note?: string): Observable<BusinessApplicant> {
    return this.api.post<BusinessApplicant>(`${this.endpoint}/applicants/${encodeURIComponent(id)}/review`, { decision, note });
  }

  refreshApplicant(id: string): Observable<BusinessApplicant> {
    return this.api.post<BusinessApplicant>(`${this.endpoint}/applicants/${encodeURIComponent(id)}/refresh`, {});
  }

  downloadDocument(applicantId: string, documentId: string): Observable<Blob> {
    return this.api.getBlob(
      `${this.endpoint}/applicants/${encodeURIComponent(applicantId)}/documents/${encodeURIComponent(documentId)}`,
      new HttpParams(),
    );
  }
}
