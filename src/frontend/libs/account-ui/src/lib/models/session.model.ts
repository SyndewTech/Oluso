export interface Session {
  id: string;
  clientId?: string;
  clientName?: string;
  /** Applications with live tokens in this session. */
  clients?: string[];
  createdAt: string;
  expiresAt?: string;
  lastActivityAt?: string;
  ipAddress?: string;
  userAgent?: string;
  location?: string;
  isCurrent: boolean;
}
