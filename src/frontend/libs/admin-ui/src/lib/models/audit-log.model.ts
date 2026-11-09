export interface AuditLog {
  id: number;
  timestamp: string;
  eventType?: string;
  action: string;
  category: string;
  subjectId?: string;
  subjectName?: string;
  subjectEmail?: string;
  clientId?: string;
  resourceType?: string;
  resourceId?: string;
  resourceName?: string;
  ipAddress?: string;
  userAgent?: string;
  success: boolean;
  errorMessage?: string;
  details?: string;
  reason?: string;
  originKind?: AuditOriginKind;
  originId?: string;
  originName?: string;
}

export interface AuditLogFilter {
  startDate?: string;
  endDate?: string;
  action?: string;
  category?: string;
  subjectId?: string;
  clientId?: string;
  originKind?: AuditOriginKind;
  originId?: string;
  success?: boolean;
  search?: string;
  from?: string;
  to?: string;
  pageNumber: number;
  pageSize: number;
}

export const AUDIT_LOG_CATEGORIES = [
  'Authentication',
  'User',
  'Security',
  'Token',
  'Client',
  'Role',
  'Resource',
  'Settings',
] as const;

export type AuditLogCategory = (typeof AUDIT_LOG_CATEGORIES)[number];

/** What caused an audited event, as recorded in its origin. */
export const AUDIT_ORIGIN_KINDS = [
  { value: 'admin', label: 'Admin' },
  { value: 'service_client', label: 'Service client' },
  { value: 'scim', label: 'SCIM' },
  { value: 'integration', label: 'Integration feed' },
  { value: 'journey', label: 'Journey' },
  { value: 'user', label: 'User (self-service)' },
  { value: 'anonymous', label: 'Anonymous' },
  { value: 'system', label: 'System' },
] as const;

export type AuditOriginKind = (typeof AUDIT_ORIGIN_KINDS)[number]['value'];
