import { AuthUser } from './auth-user.model';
import { OrganizationContext } from './organization-context.model';

export interface AuthState {
  user: AuthUser | null;
  accessToken: string | null;
  refreshToken?: string | null;
  idToken?: string | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  error: string | null;
  currentTenantId: string | null;
  currentOrganization: OrganizationContext | null;
  isSuperAdmin: boolean;
}
