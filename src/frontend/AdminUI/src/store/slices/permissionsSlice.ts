import { create } from 'zustand';
import { permissionsService } from '../../services/permissionsService';
import type { MyPermissionsResponse, PermissionsResponse } from '../../types/permissions';

// Cache key for localStorage
const PERMISSIONS_CACHE_KEY = 'oluso_permissions_cache';

interface CachedPermissions {
  effectivePermissions: string[];
  rolePermissions: Record<string, string[]>;
  isSuperAdmin: boolean;
  timestamp: number;
}

// Helper functions for localStorage caching
function getCachedPermissions(): CachedPermissions | null {
  try {
    const cached = localStorage.getItem(PERMISSIONS_CACHE_KEY);
    if (!cached) return null;

    const parsed: CachedPermissions = JSON.parse(cached);
    // We return cached data even if expired - better to show stale permissions
    // than to hide all navigation items. The isFromCache flag will indicate
    // that this data may be stale. Cache expiry is tracked via timestamp.
    return parsed;
  } catch {
    return null;
  }
}

function setCachedPermissions(data: Omit<CachedPermissions, 'timestamp'>): void {
  try {
    const cached: CachedPermissions = {
      ...data,
      timestamp: Date.now(),
    };
    localStorage.setItem(PERMISSIONS_CACHE_KEY, JSON.stringify(cached));
  } catch {
    // Ignore localStorage errors (e.g., quota exceeded, private browsing)
  }
}

function clearCachedPermissions(): void {
  try {
    localStorage.removeItem(PERMISSIONS_CACHE_KEY);
  } catch {
    // Ignore localStorage errors
  }
}

interface PermissionsState {
  // User's effective permissions
  effectivePermissions: Set<string>;
  rolePermissions: Record<string, string[]>;
  isSuperAdmin: boolean;

  // All available permissions (for role management)
  availablePermissions: PermissionsResponse | null;

  // Loading states
  isLoading: boolean;
  isLoaded: boolean;
  isFromCache: boolean; // Indicates if current data is from cache (possibly stale)
  isOffline: boolean; // Indicates API is not reachable
  error: string | null;

  // Actions
  fetchMyPermissions: () => Promise<void>;
  fetchAvailablePermissions: () => Promise<void>;
  hasPermission: (permission: string) => boolean;
  hasAnyPermission: (permissions: string[]) => boolean;
  hasAllPermissions: (permissions: string[]) => boolean;
  reset: () => void;
}

export const usePermissionsStore = create<PermissionsState>((set, get) => ({
  effectivePermissions: new Set<string>(),
  rolePermissions: {},
  isSuperAdmin: false,
  availablePermissions: null,
  isLoading: false,
  isLoaded: false,
  isFromCache: false,
  isOffline: false,
  error: null,

  fetchMyPermissions: async () => {
    set({ isLoading: true, error: null });
    try {
      const response: MyPermissionsResponse = await permissionsService.getMyPermissions();

      // Cache the permissions for offline resilience
      setCachedPermissions({
        effectivePermissions: response.effectivePermissions,
        rolePermissions: response.rolePermissions,
        isSuperAdmin: response.isSuperAdmin,
      });

      set({
        effectivePermissions: new Set(response.effectivePermissions),
        rolePermissions: response.rolePermissions,
        isSuperAdmin: response.isSuperAdmin,
        isLoading: false,
        isLoaded: true,
        isFromCache: false,
        isOffline: false,
      });
    } catch (error) {
      // On failure, try to restore from cache
      const cached = getCachedPermissions();
      if (cached) {
        set({
          effectivePermissions: new Set(cached.effectivePermissions),
          rolePermissions: cached.rolePermissions,
          isSuperAdmin: cached.isSuperAdmin,
          isLoading: false,
          isLoaded: true,
          isFromCache: true,
          isOffline: true,
          error: 'Using cached permissions - API unavailable',
        });
      } else {
        set({
          isLoading: false,
          isOffline: true,
          error: error instanceof Error ? error.message : 'Failed to fetch permissions',
        });
      }
    }
  },

  fetchAvailablePermissions: async () => {
    try {
      const response = await permissionsService.getAll();
      set({ availablePermissions: response });
    } catch (error) {
      console.error('Failed to fetch available permissions:', error);
    }
  },

  hasPermission: (permission: string) => {
    const state = get();

    // While permissions are loading, be permissive to avoid hiding navigation
    // Once loaded, enforce actual permissions
    if (!state.isLoaded && !state.isOffline) {
      return true;
    }

    // SuperAdmins have all permissions
    if (state.isSuperAdmin) {
      return true;
    }

    // Check exact match
    if (state.effectivePermissions.has(permission)) {
      return true;
    }

    // Check category wildcard (e.g., users.* grants users.read)
    const category = permission.split('.')[0];
    if (state.effectivePermissions.has(`${category}.*`)) {
      return true;
    }

    // Check full wildcard
    if (state.effectivePermissions.has('*')) {
      return true;
    }

    return false;
  },

  hasAnyPermission: (permissions: string[]) => {
    const { hasPermission } = get();
    return permissions.some(hasPermission);
  },

  hasAllPermissions: (permissions: string[]) => {
    const { hasPermission } = get();
    return permissions.every(hasPermission);
  },

  reset: () => {
    // Clear cached permissions on logout/reset
    clearCachedPermissions();
    set({
      effectivePermissions: new Set<string>(),
      rolePermissions: {},
      isSuperAdmin: false,
      availablePermissions: null,
      isLoading: false,
      isLoaded: false,
      isFromCache: false,
      isOffline: false,
      error: null,
    });
  },
}));
