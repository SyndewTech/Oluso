import { createContext, useContext, useEffect, useCallback, type ReactNode } from 'react';
import { useAuth as useOidcAuth, hasAuthParams } from 'react-oidc-context';
import type { User } from 'oidc-client-ts';
import type { WorkspaceUser } from '@oluso/ui-core';
import { useAuthStore } from '../store/slices/authSlice';
import { apiClient } from '../services/api';

interface AuthContextValue {
  user: WorkspaceUser | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  isManager: boolean;
  permissions: string[];
  features: string[];
  login: () => Promise<void>;
  logout: () => Promise<void>;
  hasPermission: (permission: string) => boolean;
  hasAnyPermission: (permissions: string[]) => boolean;
  hasFeature: (feature: string) => boolean;
}

const AuthContext = createContext<AuthContextValue | null>(null);

/**
 * Map OIDC user to WorkspaceUser
 */
async function mapOidcUserToWorkspaceUser(oidcUser: User): Promise<WorkspaceUser> {
  // Get employee info from backend to determine manager status
  let employeeId: string | undefined;
  let isManager = false;

  try {
    const response = await apiClient.get('/api/workspace/me');
    employeeId = response.data.employeeId;
    isManager = response.data.isManager ?? false;
  } catch {
    // User might not have an employee record yet
  }

  const profile = oidcUser.profile;

  return {
    id: profile.sub,
    userName: profile.preferred_username || profile.email || profile.sub,
    email: profile.email || '',
    displayName: profile.name || `${profile.given_name || ''} ${profile.family_name || ''}`.trim() || profile.email,
    firstName: profile.given_name,
    lastName: profile.family_name,
    profilePictureUrl: profile.picture,
    employeeId,
    isManager,
    roles: (profile.roles as string[]) || [],
    permissions: (profile.permissions as string[]) || [],
    tenantId: (profile.tenant_id as string) || '',
  };
}

interface AuthProviderProps {
  children: ReactNode;
}

export function AuthProvider({ children }: AuthProviderProps) {
  const oidcAuth = useOidcAuth();
  const { setUser, setTokens, setLoading, logout: storeLogout, user, isAuthenticated, hasHydrated } = useAuthStore();

  // Handle OIDC callback and user changes
  useEffect(() => {
    if (!hasHydrated) return;

    const handleAuth = async () => {
      // Handle silent callback
      if (hasAuthParams()) {
        return; // Let OIDC library handle the callback
      }

      if (oidcAuth.isLoading) {
        setLoading(true);
        return;
      }

      if (oidcAuth.error) {
        console.error('OIDC error:', oidcAuth.error);
        setLoading(false);
        return;
      }

      if (oidcAuth.isAuthenticated && oidcAuth.user) {
        // Map OIDC user to workspace user
        const workspaceUser = await mapOidcUserToWorkspaceUser(oidcAuth.user);
        setUser(workspaceUser);
        setTokens(
          oidcAuth.user.access_token,
          oidcAuth.user.refresh_token || null,
          oidcAuth.user.expires_at ? oidcAuth.user.expires_at * 1000 : null
        );
      } else if (!oidcAuth.isAuthenticated && isAuthenticated) {
        // User logged out from OIDC
        storeLogout();
      }

      setLoading(false);
    };

    handleAuth();
  }, [oidcAuth.isAuthenticated, oidcAuth.isLoading, oidcAuth.user, oidcAuth.error, hasHydrated]);

  // Listen for unauthorized events
  useEffect(() => {
    const handleUnauthorized = () => {
      storeLogout();
      oidcAuth.signinRedirect();
    };

    window.addEventListener('auth:unauthorized', handleUnauthorized);
    return () => window.removeEventListener('auth:unauthorized', handleUnauthorized);
  }, [oidcAuth]);

  const login = useCallback(async () => {
    await oidcAuth.signinRedirect();
  }, [oidcAuth]);

  const logout = useCallback(async () => {
    storeLogout();
    await oidcAuth.signoutRedirect();
  }, [oidcAuth, storeLogout]);

  const hasPermission = useCallback(
    (permission: string) => {
      return user?.permissions.includes(permission) ?? false;
    },
    [user]
  );

  const hasAnyPermission = useCallback(
    (permissions: string[]) => {
      return permissions.some((p) => user?.permissions.includes(p));
    },
    [user]
  );

  // Features come from the OIDC profile (tenant features)
  const features = (oidcAuth.user?.profile?.features as string[]) || [];

  const hasFeature = useCallback(
    (feature: string) => {
      // If no features specified, allow all (feature flag disabled)
      if (features.length === 0) return true;
      return features.includes(feature);
    },
    [features]
  );

  const value: AuthContextValue = {
    user,
    isAuthenticated,
    isLoading: oidcAuth.isLoading || !hasHydrated,
    isManager: user?.isManager ?? false,
    permissions: user?.permissions ?? [],
    features,
    login,
    logout,
    hasPermission,
    hasAnyPermission,
    hasFeature,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}
