import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type { WorkspaceUser } from '@oluso/ui-core';
import { setAuthToken, setTenantId } from '../../services/api';

export interface AuthState {
  // User information
  user: WorkspaceUser | null;
  accessToken: string | null;
  refreshToken: string | null;
  expiresAt: number | null;

  // State flags
  isAuthenticated: boolean;
  isLoading: boolean;
  hasHydrated: boolean;

  // Actions
  setUser: (user: WorkspaceUser | null) => void;
  setTokens: (accessToken: string | null, refreshToken?: string | null, expiresAt?: number | null) => void;
  setLoading: (loading: boolean) => void;
  setHasHydrated: (hydrated: boolean) => void;
  logout: () => void;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set, get) => ({
      user: null,
      accessToken: null,
      refreshToken: null,
      expiresAt: null,
      isAuthenticated: false,
      isLoading: true,
      hasHydrated: false,

      setUser: (user) => {
        set({ user, isAuthenticated: !!user });
        if (user?.tenantId) {
          setTenantId(user.tenantId);
        }
      },

      setTokens: (accessToken, refreshToken = null, expiresAt = null) => {
        set({ accessToken, refreshToken, expiresAt });
        setAuthToken(accessToken);
      },

      setLoading: (isLoading) => set({ isLoading }),

      setHasHydrated: (hasHydrated) => {
        set({ hasHydrated });
        // Re-apply token from persisted state after hydration
        const { accessToken, user } = get();
        if (accessToken) {
          setAuthToken(accessToken);
        }
        if (user?.tenantId) {
          setTenantId(user.tenantId);
        }
      },

      logout: () => {
        set({
          user: null,
          accessToken: null,
          refreshToken: null,
          expiresAt: null,
          isAuthenticated: false,
        });
        setAuthToken(null);
        setTenantId(null);
      },
    }),
    {
      name: 'workspace-auth',
      partialize: (state) => ({
        user: state.user,
        accessToken: state.accessToken,
        refreshToken: state.refreshToken,
        expiresAt: state.expiresAt,
        isAuthenticated: state.isAuthenticated,
      }),
      onRehydrateStorage: () => (state) => {
        state?.setHasHydrated(true);
      },
    }
  )
);

// Selector hooks
export const useUser = () => useAuthStore((state) => state.user);
export const useIsAuthenticated = () => useAuthStore((state) => state.isAuthenticated);
export const useIsManager = () => useAuthStore((state) => state.user?.isManager ?? false);
export const usePermissions = () => useAuthStore((state) => state.user?.permissions ?? []);
