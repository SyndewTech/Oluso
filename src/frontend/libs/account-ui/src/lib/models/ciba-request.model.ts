export interface CibaRequest {
  authReqId: string;
  clientId: string;
  clientName?: string;
  clientLogoUri?: string;
  bindingMessage?: string;
  requestedScopes: string[];
  status: 'pending' | 'approved' | 'denied' | 'expired';
  createdAt: string;
  expiresAt: string;
}
