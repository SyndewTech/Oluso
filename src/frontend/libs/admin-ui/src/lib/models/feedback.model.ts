export interface FeedbackDto {
  id: string;
  title: string;
  description: string;
  category: string;
  url?: string;
  hasScreenshot: boolean;
  elementSelector?: string;
  userAgent?: string;
  viewportSize?: string;
  status: string;
  submittedByUserId: string;
  submittedByEmail?: string;
  submittedByName?: string;
  portal: string;
  tenantId?: string;
  tenantName?: string;
  createdAt: string;
}

export interface FeedbackFilter {
  pageNumber: number;
  pageSize: number;
  category?: string;
  status?: string;
}

export const FEEDBACK_CATEGORIES = ['bug', 'feature', 'note'] as const;
export type FeedbackCategory = (typeof FEEDBACK_CATEGORIES)[number];

export const FEEDBACK_STATUSES = ['new', 'reviewed', 'resolved', 'dismissed'] as const;
export type FeedbackStatus = (typeof FEEDBACK_STATUSES)[number];
