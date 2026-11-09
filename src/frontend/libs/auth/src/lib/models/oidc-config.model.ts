export interface OidcConfig {
  authority: string;
  clientId: string;
  redirectUri: string;
  postLogoutRedirectUri?: string;
  scope?: string;
  responseType?: string;
  silentRedirectUri?: string;
  automaticSilentRenew?: boolean;
  loadUserInfo?: boolean;
}
