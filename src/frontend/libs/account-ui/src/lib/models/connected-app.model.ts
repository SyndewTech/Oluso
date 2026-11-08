export interface ConnectedApp {
  clientId: string;
  clientName: string;
  clientUri?: string;
  logoUri?: string;
  description?: string;
  firstConnectedAt: string;
  lastUsedAt: string;
  scopes: string[];
  hasActiveTokens: boolean;
}
