export interface AuditLog {
  id: number;
  timestamp: string;
  action: string;
  category: string;
  subjectId?: string;
  subjectName?: string;
  clientId?: string;
  resourceType?: string;
  resourceId?: string;
  resourceName?: string;
  ipAddress?: string;
  userAgent?: string;
  success: boolean;
  errorMessage?: string;
  details?: string;
}

export interface AuditLogFilter {
  startDate?: string;
  endDate?: string;
  action?: string;
  category?: string;
  subjectId?: string;
  clientId?: string;
  success?: boolean;
  pageNumber: number;
  pageSize: number;
}

export interface PaginatedResult<T> {
  items: T[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}
