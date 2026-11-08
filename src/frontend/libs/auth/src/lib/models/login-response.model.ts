import { AuthUser } from './auth-user.model';

export interface LoginResponse {
  user: AuthUser;
  accessToken: string;
  refreshToken?: string;
  idToken?: string;
  expiresIn?: number;
}
