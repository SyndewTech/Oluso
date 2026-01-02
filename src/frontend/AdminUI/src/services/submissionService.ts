import api from './api';

// Types
export interface SubmissionMetadata {
  ipAddress?: string;
  userAgent?: string;
  referrer?: string;
  utmParameters?: Record<string, string>;
  country?: string;
  locale?: string;
}

export type SubmissionStatus =
  | 'New'
  | 'Reviewed'
  | 'Processing'
  | 'Approved'
  | 'Rejected'
  | 'FollowUp'
  | 'Archived';

export interface JourneySubmission {
  id: string;
  policyId: string;
  policyName?: string;
  tenantId?: string;
  journeyId?: string;
  data: Record<string, unknown>;
  metadata: SubmissionMetadata;
  status: SubmissionStatus;
  notes?: string;
  tags?: string[];
  createdAt: string;
  reviewedAt?: string;
  reviewedBy?: string;
}

export interface SubmissionListResponse {
  submissions: JourneySubmission[];
  total: number;
  skip: number;
  take: number;
}

export interface DataCollectionPolicy {
  id: string;
  name: string;
  description?: string;
  type: string;
  enabled: boolean;
  submissionCount: number;
  maxSubmissions: number;
  allowDuplicates: boolean;
  createdAt: string;
}

export interface UpdateSubmissionRequest {
  status?: SubmissionStatus;
  notes?: string;
  tags?: string[];
}

export interface ExportOptions {
  from?: string;
  to?: string;
  format?: 'json' | 'csv';
}

// API Service
export const submissionService = {
  // Get all data collection policies with submission counts
  async getDataCollectionPolicies(): Promise<DataCollectionPolicy[]> {
    const response = await api.get('/submissions/policies');
    return response.data;
  },

  // Get submissions for a policy
  async getSubmissions(
    policyId: string,
    skip = 0,
    take = 50
  ): Promise<SubmissionListResponse> {
    const response = await api.get(`/submissions/policy/${policyId}`, {
      params: { skip, take }
    });
    return response.data;
  },

  // Get a single submission
  async getSubmission(submissionId: string): Promise<JourneySubmission> {
    const response = await api.get(`/submissions/${submissionId}`);
    return response.data;
  },

  // Update a submission (status, notes, tags)
  async updateSubmission(
    submissionId: string,
    request: UpdateSubmissionRequest
  ): Promise<JourneySubmission> {
    const response = await api.patch(`/submissions/${submissionId}`, request);
    return response.data;
  },

  // Delete a submission
  async deleteSubmission(submissionId: string): Promise<void> {
    await api.delete(`/submissions/${submissionId}`);
  },

  // Export submissions
  async exportSubmissions(
    policyId: string,
    options?: ExportOptions
  ): Promise<Blob> {
    const response = await api.get(`/submissions/policy/${policyId}/export`, {
      params: options,
      responseType: 'blob'
    });
    return response.data;
  },

  // Get submission count for a policy
  async getSubmissionCount(policyId: string): Promise<number> {
    const response = await api.get(`/submissions/policy/${policyId}/count`);
    return response.data.count;
  }
};

export default submissionService;
