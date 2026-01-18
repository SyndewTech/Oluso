import { create } from 'zustand';
import { persist } from 'zustand/middleware';

interface User {
  id: string;
  userName: string;
  email: string;
  displayName?: string;
  roles: string[];
  tenantId?: string | null;
}

interface OrganizationContext {
  id: string;
  name?: string;
  slug?: string;
  role: string;
}

interface AuthState {
  user: User | null;
  accessToken: string | null;
  currentTenantId: string | null; // For SuperAdmins to specify which tenant to operate in
  currentOrganization: OrganizationContext | null; // Current organization context
  isAuthenticated: boolean;
  hasHydrated: boolean;
  setUser: (user: User | null) => void;
  setAccessToken: (token: string | null) => void;
  setCurrentTenantId: (tenantId: string | null) => void;
  setCurrentOrganization: (org: OrganizationContext | null) => void;
  login: (user: User, token: string, tenantId?: string | null) => void;
  logout: () => void;
  setHasHydrated: (state: boolean) => void;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      user: null,
      accessToken: null,
      currentTenantId: null,
      currentOrganization: null,
      isAuthenticated: false,
      hasHydrated: false,
      setUser: (user) => set({ user, isAuthenticated: !!user }),
      setAccessToken: (accessToken) => set({ accessToken }),
      setCurrentTenantId: (currentTenantId) => set({ currentTenantId }),
      setCurrentOrganization: (currentOrganization) => set({ currentOrganization }),
      login: (user, accessToken, tenantId) =>
        set({
          user,
          accessToken,
          currentTenantId: tenantId ?? user.tenantId ?? null,
          currentOrganization: null,
          isAuthenticated: true
        }),
      logout: () =>
        set({ user: null, accessToken: null, currentTenantId: null, currentOrganization: null, isAuthenticated: false }),
      setHasHydrated: (hasHydrated) => set({ hasHydrated }),
    }),
    {
      name: 'auth-storage',
      partialize: (state) => ({
        user: state.user,
        accessToken: state.accessToken,
        currentTenantId: state.currentTenantId,
        currentOrganization: state.currentOrganization,
        isAuthenticated: state.isAuthenticated,
      }),
      onRehydrateStorage: () => (state) => {
        state?.setHasHydrated(true);
      },
    }
  )
);
