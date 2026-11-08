import { Injectable, signal, computed } from '@angular/core';

/**
 * Response from GET /permissions/my endpoint
 */
export interface MyPermissionsResponse {
  effectivePermissions: string[];
  rolePermissions: Record<string, string[]>;
  isSuperAdmin: boolean;
  explicitGrantPermissions: string[];
}

/**
 * Cached permissions structure
 */
interface CachedPermissions {
  /** Identity the entry belongs to (`userId|tenantId`); entries without it are never used. */
  identity?: string;
  effectivePermissions: string[];
  rolePermissions: Record<string, string[]>;
  isSuperAdmin: boolean;
  explicitGrantPermissions: string[];
  timestamp: number;
}

/**
 * Permissions Service
 *
 * Manages user permissions with support for:
 * - Individual permission checks
 * - Wildcard permissions (e.g., 'users.*', '*')
 * - Role-based permissions
 * - Offline caching, scoped to the signed-in user + tenant
 *
 * Two levels of trust:
 * - `hasPermission` & co. are UI hints: while nothing is loaded they answer
 *   `true` so navigation does not flicker, and a same-user cache entry is
 *   shown until fresh permissions arrive.
 * - `isAuthoritative` / `isSettled` are for enforcement (route guards): they
 *   only report permissions freshly loaded for the current identity, or the
 *   current identity's cache while the API is unreachable (offline mode).
 *
 * Apps must call `useIdentity(userId, tenantId)` once the signed-in identity
 * is known, before loading permissions; the cache is only read and written
 * for that identity.
 */
@Injectable({
  providedIn: 'root',
})
export class PermissionsService {
  private readonly _permissions = signal<Set<string>>(new Set());
  private readonly _rolePermissions = signal<Record<string, string[]>>({});
  private readonly _isSuperAdmin = signal<boolean>(false);
  private readonly _explicitGrantPermissions = signal<Set<string>>(new Set());
  private readonly _isLoading = signal<boolean>(false);
  private readonly _isLoaded = signal<boolean>(false);
  private readonly _isFromCache = signal<boolean>(false);
  private readonly _isOffline = signal<boolean>(false);
  private readonly _isFailed = signal<boolean>(false);
  private readonly _identity = signal<string | null>(null);

  /** Read-only access to permissions */
  readonly permissions = computed(() => Array.from(this._permissions()));
  readonly isSuperAdmin = this._isSuperAdmin.asReadonly();
  readonly isLoading = this._isLoading.asReadonly();
  readonly isLoaded = this._isLoaded.asReadonly();
  readonly isFromCache = this._isFromCache.asReadonly();
  readonly isOffline = this._isOffline.asReadonly();

  /**
   * True when the loaded permissions can be enforced: freshly loaded for the
   * current identity, or that identity's cache while offline.
   */
  readonly isAuthoritative = computed(
    () => this._isLoaded() && (!this._isFromCache() || this._isOffline())
  );

  /**
   * True once enforcement can decide: either authoritative permissions are
   * present, or loading failed / was cleared (deny).
   */
  readonly isSettled = computed(() => this.isAuthoritative() || this._isFailed());

  private readonly CACHE_KEY = 'oluso_permissions_cache';

  /**
   * Declare the signed-in identity. Switching to a different identity drops
   * any permissions held for the previous one and shows this identity's cache
   * (if any) as a UI hint until fresh permissions are loaded. Calling it again
   * with the same identity is a no-op.
   *
   * @returns true when the identity changed (permissions must be reloaded).
   */
  useIdentity(userId: string, tenantId?: string | null): boolean {
    if (!userId) {
      return false;
    }
    const identity = `${userId}|${tenantId ?? ''}`;
    if (this._identity() === identity) {
      return false;
    }
    this.resetState();
    this._identity.set(identity);
    this.loadFromCache();
    return true;
  }

  /**
   * Set permissions from /permissions/my API response
   */
  setMyPermissions(response: MyPermissionsResponse): void {
    this._permissions.set(new Set(response.effectivePermissions));
    this._rolePermissions.set(response.rolePermissions);
    this._isSuperAdmin.set(response.isSuperAdmin);
    this._explicitGrantPermissions.set(
      new Set(response.explicitGrantPermissions ?? [])
    );
    this._isLoaded.set(true);
    this._isLoading.set(false);
    this._isFromCache.set(false);
    this._isFailed.set(false);
    this.saveToCache(response);
  }

  /**
   * Initialize permissions from login response user data.
   * This provides immediate access to permissions after login without waiting
   * for the /permissions/my API call.
   *
   * @param permissions - Array of permission strings from login response
   * @param roles - Array of role names from login response
   */
  initFromLoginResponse(permissions: string[], roles: string[]): void {
    // Check if user has SuperAdmin or SystemAdmin role
    const isSuperAdmin = roles.some(
      (r) =>
        r.toLowerCase() === 'superadmin' || r.toLowerCase() === 'systemadmin'
    );

    this._permissions.set(new Set(permissions));
    this._isSuperAdmin.set(isSuperAdmin);
    this._isLoaded.set(true);
    this._isLoading.set(false);
    this._isFromCache.set(false);
    this._isFailed.set(false);

    // Build role permissions map (each role maps to the full permissions list
    // since we don't have per-role breakdown from login response)
    const rolePermissions: Record<string, string[]> = {};
    if (isSuperAdmin) {
      rolePermissions['SuperAdmin'] = ['*'];
    }

    this._rolePermissions.set(rolePermissions);

    // Cache the permissions (explicit grant list not available from login response;
    // it will be populated when /permissions/my is fetched)
    this.saveToCache({
      effectivePermissions: permissions,
      rolePermissions,
      isSuperAdmin,
      explicitGrantPermissions: [],
    });
  }

  /**
   * Set loading state
   */
  setLoading(isLoading: boolean): void {
    this._isLoading.set(isLoading);
  }

  /**
   * Mark as loaded from cache (when API fails)
   */
  setFromCache(isFromCache: boolean): void {
    this._isFromCache.set(isFromCache);
  }

  /**
   * Set offline state (when API is unreachable)
   */
  setOffline(isOffline: boolean): void {
    this._isOffline.set(isOffline);
  }

  /**
   * Check if user has a specific permission
   * Supports wildcard patterns: 'users.*' matches 'users.read', 'users.write', etc.
   */
  hasPermission(permission: string): boolean {
    // While permissions are loading, be permissive to avoid hiding navigation
    // Once loaded, enforce actual permissions
    if (!this._isLoaded() && !this._isOffline()) {
      return true;
    }

    // No role-based bypass — all roles (including SuperAdmin) use their assigned
    // permissions. SuperAdmin/TenantAdmin get "*" wildcard which is checked below.
    // The isSuperAdmin flag is only used for superAdminOnly nav item visibility.

    const permissions = this._permissions();

    // Direct match always works
    if (permissions.has(permission)) {
      return true;
    }

    // Explicit-grant permissions cannot be matched by wildcards.
    // If this permission requires explicit grant and wasn't directly matched above, deny.
    if (this._explicitGrantPermissions().has(permission)) {
      return false;
    }

    // Check for global wildcard
    if (permissions.has('*')) {
      return true;
    }

    // Wildcards at any depth, as the server checks them: 'emr.*' and 'emr.roles.*' both match 'emr.roles.read'
    const segments = permission.split('.');
    for (let i = 1; i < segments.length; i++) {
      if (permissions.has(`${segments.slice(0, i).join('.')}.*`)) {
        return true;
      }
    }

    return false;
  }

  /**
   * Check if user has any of the specified permissions
   */
  hasAnyPermission(requiredPermissions: string[]): boolean {
    return requiredPermissions.some((permission) => this.hasPermission(permission));
  }

  /**
   * Check if user has all of the specified permissions
   */
  hasAllPermissions(requiredPermissions: string[]): boolean {
    return requiredPermissions.every((permission) => this.hasPermission(permission));
  }

  /**
   * Get all permissions for a specific role
   */
  getRolePermissions(role: string): string[] {
    return this._rolePermissions()[role] ?? [];
  }

  /**
   * Clear all permissions (on logout)
   */
  clear(): void {
    this.resetState();
    this._identity.set(null);
    this._isFailed.set(true);
    this.clearCache();
  }

  /**
   * Load the current identity's permissions from the localStorage cache.
   * Does nothing (returns false) when no identity is set or the cached entry
   * belongs to someone else; a foreign entry is removed. Cached permissions
   * only become authoritative while offline (see `setOffline`).
   */
  loadFromCache(): boolean {
    const identity = this._identity();
    if (!identity) {
      return false;
    }
    try {
      const cached = localStorage.getItem(this.CACHE_KEY);
      if (!cached) {
        return false;
      }
      const data = JSON.parse(cached) as CachedPermissions;
      if (data.identity !== identity) {
        this.clearCache();
        return false;
      }
      this._permissions.set(new Set(data.effectivePermissions));
      this._rolePermissions.set(data.rolePermissions);
      this._isSuperAdmin.set(data.isSuperAdmin);
      this._explicitGrantPermissions.set(
        new Set(data.explicitGrantPermissions ?? [])
      );
      this._isLoaded.set(true);
      this._isFromCache.set(true);
      return true;
    } catch {
      return false;
    }
  }

  private resetState(): void {
    this._permissions.set(new Set());
    this._rolePermissions.set({});
    this._isSuperAdmin.set(false);
    this._explicitGrantPermissions.set(new Set());
    this._isLoaded.set(false);
    this._isLoading.set(false);
    this._isFromCache.set(false);
    this._isOffline.set(false);
    this._isFailed.set(false);
  }

  /**
   * Save permissions to localStorage cache
   */
  private saveToCache(response: MyPermissionsResponse): void {
    const identity = this._identity();
    if (!identity) {
      return;
    }
    try {
      const cached: CachedPermissions = {
        identity,
        effectivePermissions: response.effectivePermissions,
        rolePermissions: response.rolePermissions,
        isSuperAdmin: response.isSuperAdmin,
        explicitGrantPermissions: response.explicitGrantPermissions ?? [],
        timestamp: Date.now(),
      };
      localStorage.setItem(this.CACHE_KEY, JSON.stringify(cached));
    } catch {
      // Ignore cache errors
    }
  }

  /**
   * Clear localStorage cache
   */
  private clearCache(): void {
    try {
      localStorage.removeItem(this.CACHE_KEY);
    } catch {
      // Ignore cache errors
    }
  }
}
