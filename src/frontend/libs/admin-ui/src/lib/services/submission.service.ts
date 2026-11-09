import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { HttpParams } from '@angular/common/http';
import { ApiService } from './api.service';
import {
  JourneySubmission,
  DataCollectionPolicy,
  SubmissionListResponse,
  UpdateSubmissionRequest,
  BulkUpdateSubmissionsRequest,
  ExportOptions,
  SubmissionFilterOptions,
  SubmissionStatistics,
  SubmissionStatus,
} from '../models/submission.model';

@Injectable({
  providedIn: 'root',
})
export class SubmissionService {
  private readonly api = inject(ApiService);

  /**
   * Get all data collection policies with submission counts
   */
  getDataCollectionPolicies(): Observable<DataCollectionPolicy[]> {
    return this.api.get<DataCollectionPolicy[]>('/submissions/policies');
  }

  /**
   * Get submissions for a policy with optional filters
   */
  getSubmissions(
    policyId: string,
    skip = 0,
    take = 50,
    filters?: SubmissionFilterOptions
  ): Observable<SubmissionListResponse> {
    const params: Record<string, string | number | boolean> = { skip, take };

    if (filters) {
      if (filters.status && filters.status !== 'all') {
        params['status'] = filters.status;
      }
      if (filters.search) {
        params['search'] = filters.search;
      }
      if (filters.dateFrom) {
        params['dateFrom'] = filters.dateFrom;
      }
      if (filters.dateTo) {
        params['dateTo'] = filters.dateTo;
      }
      if (filters.tags && filters.tags.length > 0) {
        params['tags'] = filters.tags.join(',');
      }
    }

    return this.api.get<SubmissionListResponse>(`/submissions/policy/${policyId}`, params);
  }

  /**
   * Get a single submission by ID
   */
  getSubmission(submissionId: string): Observable<JourneySubmission> {
    return this.api.get<JourneySubmission>(`/submissions/${submissionId}`);
  }

  /**
   * Update a submission (status, notes, tags)
   */
  updateSubmission(
    submissionId: string,
    request: UpdateSubmissionRequest
  ): Observable<JourneySubmission> {
    return this.api.patch<JourneySubmission>(`/submissions/${submissionId}`, request);
  }

  /**
   * Bulk update multiple submissions
   */
  bulkUpdateSubmissions(request: BulkUpdateSubmissionsRequest): Observable<{ updated: number }> {
    return this.api.post<{ updated: number }>('/submissions/bulk-update', request);
  }

  /**
   * Delete a submission
   */
  deleteSubmission(submissionId: string): Observable<void> {
    return this.api.delete<void>(`/submissions/${submissionId}`);
  }

  /**
   * Bulk delete multiple submissions
   */
  bulkDeleteSubmissions(submissionIds: string[]): Observable<{ deleted: number }> {
    return this.api.post<{ deleted: number }>('/submissions/bulk-delete', { submissionIds });
  }

  /**
   * Export submissions as file (CSV or JSON)
   */
  exportSubmissions(policyId: string, options?: ExportOptions): Observable<Blob> {
    let params = new HttpParams();

    if (options) {
      if (options.from) params = params.set('from', options.from);
      if (options.to) params = params.set('to', options.to);
      if (options.format) params = params.set('format', options.format);
      if (options.statuses) params = params.set('statuses', options.statuses.join(','));
      if (options.includeMetadata !== undefined) {
        params = params.set('includeMetadata', String(options.includeMetadata));
      }
    }

    return this.api.getBlob(`/submissions/policy/${policyId}/export`, params);
  }

  /**
   * Get submission count for a policy
   */
  getSubmissionCount(policyId: string): Observable<number> {
    return this.api.get<{ count: number }>(`/submissions/policy/${policyId}/count`).pipe(
      map((res) => res.count)
    );
  }

  /**
   * Get submission statistics for a policy
   */
  getSubmissionStatistics(policyId: string): Observable<SubmissionStatistics> {
    return this.api.get<SubmissionStatistics>(`/submissions/policy/${policyId}/statistics`);
  }

  /**
   * Get all unique tags used across submissions for a policy
   */
  getAvailableTags(policyId: string): Observable<string[]> {
    return this.api.get<string[]>(`/submissions/policy/${policyId}/tags`);
  }

  /**
   * Quick status update helper
   */
  updateStatus(submissionId: string, status: SubmissionStatus): Observable<JourneySubmission> {
    return this.updateSubmission(submissionId, { status });
  }

  /**
   * Add notes to a submission
   */
  addNotes(submissionId: string, notes: string): Observable<JourneySubmission> {
    return this.updateSubmission(submissionId, { notes });
  }

  /**
   * Add tags to a submission
   */
  addTags(submissionId: string, tags: string[]): Observable<JourneySubmission> {
    return this.updateSubmission(submissionId, { tags });
  }
}
