export interface TokenPayload {
  sub: string;
  iss: string;
  aud: string | string[];
  exp: number;
  iat: number;
  email?: string;
  name?: string;
  roles?: string[];
  permissions?: string[];
  tenant_id?: string;
  [key: string]: unknown;
}
