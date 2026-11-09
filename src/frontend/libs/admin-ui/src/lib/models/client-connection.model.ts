/**
 * Read-only "everything an integrator needs to wire this client up" view.
 * Returned by GET /api/admin/clients/{id}/connection. Issuer is resolved per
 * tenant on the server (CustomDomain / TokenSettings.IssuerUri overrides
 * factor in), and the *Endpoint URLs are derived from it so they match what
 * /.well-known/openid-configuration actually serves for this tenant.
 */
export interface ClientConnection {
  clientId: string;
  clientName?: string;
  requireClientSecret: boolean;
  requirePkce: boolean;
  allowedGrantTypes: string[];
  allowedScopes: string[];
  redirectUris: string[];
  postLogoutRedirectUris: string[];
  allowedCorsOrigins: string[];

  issuerUri: string;
  discoveryUrl: string;
  jwksUri: string;
  authorizationEndpoint: string;
  tokenEndpoint: string;
  userInfoEndpoint: string;
  endSessionEndpoint: string;
  introspectionEndpoint: string;
  revocationEndpoint: string;
}
