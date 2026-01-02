import { createContext, useContext, ReactNode, useEffect, useCallback, useState, useRef } from 'react';
import { AuthProvider as OidcAuthProvider, useAuth as useOidcAuth, hasAuthParams } from 'react-oidc-context';
import { User } from 'oidc-client-ts';
import { oidcConfig, apiBaseUrl } from '../config/oidc';
import { apiClient } from '../services/api';

interface AuthUser {
  id: string;
  email: string;
  name?: string;
  picture?: string;
  tenantIds?: string[];
}

interface AuthError {
  message: string;
  details?: string;
}

interface AuthContextValue {
  user: AuthUser | null;
  oidcUser: User | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  accessToken: string | null;
  error: AuthError | null;
  login: () => void;
  logout: () => void;
  refreshUser: () => Promise<void>;
  clearError: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

function mapOidcUserToAuthUser(oidcUser: User | null): AuthUser | null {
  if (!oidcUser?.profile) return null;

  return {
    id: oidcUser.profile.sub,
    email: oidcUser.profile.email || '',
    name: oidcUser.profile.name || oidcUser.profile.preferred_username,
    picture: oidcUser.profile.picture,
    tenantIds: oidcUser.profile.tenant_ids as string[] | undefined,
  };
}

function AuthContextProvider({ children }: { children: ReactNode }) {
  const auth = useOidcAuth();
  const [error, setError] = useState<AuthError | null>(null);
  const hasTriedSignIn = useRef(false);

  // Set up axios interceptor for access token
  useEffect(() => {
    const interceptor = apiClient.interceptors.request.use(
      (config) => {
        if (auth.user?.access_token) {
          config.headers.Authorization = `Bearer ${auth.user.access_token}`;
        }
        return config;
      },
      (error) => Promise.reject(error)
    );

    return () => {
      apiClient.interceptors.request.eject(interceptor);
    };
  }, [auth.user?.access_token]);

  // Handle OIDC errors
  useEffect(() => {
    if (auth.error) {
      console.error('Authentication error:', auth.error);
      setError({
        message: 'Authentication failed',
        details: auth.error.message,
      });
      // Mark that we've tried so we don't retry
      hasTriedSignIn.current = true;
    }
  }, [auth.error]);

  // Handle automatic sign-in - only once, and not if there's an error
  useEffect(() => {
    // Don't auto-redirect if:
    // - We already tried and failed
    // - There's an error
    // - We're authenticated
    // - We're loading
    // - There's an active navigator (in the middle of auth flow)
    // - We have auth params in URL (callback processing)
    // - We're on the callback page
    if (
      hasTriedSignIn.current ||
      error ||
      auth.isAuthenticated ||
      auth.isLoading ||
      auth.activeNavigator ||
      hasAuthParams() ||
      window.location.pathname.includes('/callback')
    ) {
      return;
    }

    // Try sign-in once
    hasTriedSignIn.current = true;
    auth.signinRedirect().catch((err) => {
      console.error('Sign-in redirect failed:', err);
      setError({
        message: 'Failed to redirect to login',
        details: err.message,
      });
    });
  }, [auth.isAuthenticated, auth.isLoading, auth.activeNavigator, error]);

  // Reset hasTriedSignIn when user successfully authenticates
  useEffect(() => {
    if (auth.isAuthenticated) {
      hasTriedSignIn.current = false;
      setError(null);
    }
  }, [auth.isAuthenticated]);

  const login = useCallback(() => {
    setError(null);
    hasTriedSignIn.current = false;
    auth.signinRedirect().catch((err) => {
      console.error('Sign-in redirect failed:', err);
      setError({
        message: 'Failed to redirect to login',
        details: err.message,
      });
    });
  }, [auth]);

  const logout = useCallback(() => {
    // Include id_token_hint for proper OIDC logout
    // This is required when post_logout_redirect_uri is specified
    auth.signoutRedirect({
      id_token_hint: auth.user?.id_token,
    }).catch((err) => {
      console.error('Sign-out redirect failed:', err);
      // Still clear local state on logout failure
      auth.removeUser();
    });
  }, [auth]);

  const refreshUser = useCallback(async () => {
    try {
      await auth.signinSilent();
    } catch (err) {
      console.error('Silent refresh failed:', err);
      setError({
        message: 'Session refresh failed',
        details: err instanceof Error ? err.message : 'Unknown error',
      });
    }
  }, [auth]);

  const clearError = useCallback(() => {
    setError(null);
    hasTriedSignIn.current = false;
  }, []);

  const value: AuthContextValue = {
    user: mapOidcUserToAuthUser(auth.user ?? null),
    oidcUser: auth.user ?? null,
    isAuthenticated: auth.isAuthenticated,
    isLoading: auth.isLoading,
    accessToken: auth.user?.access_token ?? null,
    error,
    login,
    logout,
    refreshUser,
    clearError,
  };

  return (
    <AuthContext.Provider value={value}>
      {children}
    </AuthContext.Provider>
  );
}

export function AuthProvider({ children }: { children: ReactNode }) {
  return (
    <OidcAuthProvider
      {...oidcConfig}
      onSigninCallback={() => {
        // Remove the code and state from the URL after sign-in
        // Navigate to /profile if on callback route, otherwise stay on current page
        const targetPath = window.location.pathname === '/callback' ? '/profile' : window.location.pathname;
        window.history.replaceState({}, document.title, targetPath);
      }}
    >
      <AuthContextProvider>{children}</AuthContextProvider>
    </OidcAuthProvider>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}

// Re-export apiBaseUrl for convenience
export { apiBaseUrl };
