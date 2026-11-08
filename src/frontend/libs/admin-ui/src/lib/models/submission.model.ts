// Submission Status
export type SubmissionStatus =
  | 'New'
  | 'Reviewed'
  | 'Processing'
  | 'Approved'
  | 'Rejected'
  | 'FollowUp'
  | 'Archived';

// All status options for select dropdowns
export const SUBMISSION_STATUS_OPTIONS: SubmissionStatus[] = [
  'New',
  'Reviewed',
  'Processing',
  'Approved',
  'Rejected',
  'FollowUp',
  'Archived',
];

// Status colors for badges
export const SUBMISSION_STATUS_COLORS: Record<SubmissionStatus, { bg: string; text: string }> = {
  New: { bg: 'bg-blue-100', text: 'text-blue-800' },
  Reviewed: { bg: 'bg-gray-100', text: 'text-gray-800' },
  Processing: { bg: 'bg-yellow-100', text: 'text-yellow-800' },
  Approved: { bg: 'bg-green-100', text: 'text-green-800' },
  Rejected: { bg: 'bg-red-100', text: 'text-red-800' },
  FollowUp: { bg: 'bg-orange-100', text: 'text-orange-800' },
  Archived: { bg: 'bg-gray-100', text: 'text-gray-500' },
};

// Submission Metadata
export interface SubmissionMetadata {
  ipAddress?: string;
  userAgent?: string;
  referrer?: string;
  utmParameters?: Record<string, string>;
  country?: string;
  locale?: string;
  deviceType?: string;
  browser?: string;
  os?: string;
}

// Journey Submission
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

// Submission List Response with pagination
export interface SubmissionListResponse {
  items: JourneySubmission[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

// Data Collection Policy (for the left panel list)
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
  /** The module that owns the journey; its answers go only to that module. */
  ownerModule?: string | null;
  ownerModuleName?: string | null;
  /** Only when each submission arrived and its status are shown, never the answers. */
  answersHidden?: boolean;
  // Statistics
  todayCount?: number;
  weekCount?: number;
  newCount?: number;
}

// Update Submission Request
export interface UpdateSubmissionRequest {
  status?: SubmissionStatus;
  notes?: string;
  tags?: string[];
}

// Bulk Update Request
export interface BulkUpdateSubmissionsRequest {
  submissionIds: string[];
  status?: SubmissionStatus;
  tags?: string[];
  addTags?: string[];
  removeTags?: string[];
}

// Export Options
export interface ExportOptions {
  from?: string;
  to?: string;
  format?: 'json' | 'csv';
  statuses?: SubmissionStatus[];
  includeMetadata?: boolean;
}

// Submission Filter Options
export interface SubmissionFilterOptions {
  status?: SubmissionStatus | 'all';
  search?: string;
  dateFrom?: string;
  dateTo?: string;
  tags?: string[];
}

// Submission Statistics
export interface SubmissionStatistics {
  total: number;
  byStatus: Record<SubmissionStatus, number>;
  today: number;
  thisWeek: number;
  thisMonth: number;
  averagePerDay: number;
  recentTrend: 'up' | 'down' | 'stable';
  trendPercentage?: number;
}
