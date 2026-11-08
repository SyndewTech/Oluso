export interface FeedbackSubmitRequest {
  title: string;
  description: string;
  category: 'bug' | 'feature' | 'note';
  url?: string;
  elementSelector?: string;
  viewportSize?: string;
}

export interface FeedbackSubmitResponse {
  id: string;
  createdAt: string;
}
