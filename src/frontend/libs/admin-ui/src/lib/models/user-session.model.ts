export interface UserSessionDto {
  sessionId: string;
  userId: string;
  clientId?: string;
  clientName?: string;
  created: string;
  expires?: string;
  ipAddress?: string;
  userAgent?: string;
  isCurrent?: boolean;
}
