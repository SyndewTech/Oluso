import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap, catchError, throwError } from 'rxjs';
import { AuthStore } from '../stores/auth.store';
import { AuthUser } from '../models/auth-user.model';
import { LoginRequest } from '../models/login-request.model';
import { LoginResponse } from '../models/login-response.model';
import { TokenPayload } from '../models/token-payload.model';

/**
 * Auth Service
 *
 * Handles authentication operations including login, logout, token refresh,
 * and user management.
 *
 * This mirrors the auth services from the React codebase.
 */
@Injectable({
  providedIn: 'root',
})
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly authStore = inject(AuthStore);

  private apiBaseUrl = '/api/admin';

  /**
   * Configure the API base URL
   */
  setApiBaseUrl(url: string): void {
    this.apiBaseUrl = url;
  }

  /**
   * Login with email and password
   */
  login(request: LoginRequest): Observable<LoginResponse> {
    this.authStore.setLoading(true);
    this.authStore.setError(null);

    return this.http.post<LoginResponse>(`${this.apiBaseUrl}/auth/login`, request).pipe(
      tap((response) => {
        this.authStore.login(
          response.user,
          response.accessToken,
          response.refreshToken,
          response.idToken
        );

        // Set organization context from login response
        const user = response.user;
        if (user.organizationIds?.length && user.organizationRoles) {
          // Pick the first org where user is owner or admin, otherwise first org
          const adminOrgId = user.organizationIds.find(
            (id) => user.organizationRoles?.[id] === 'owner' || user.organizationRoles?.[id] === 'admin'
          ) ?? user.organizationIds[0];

          const role = user.organizationRoles[adminOrgId];
          this.authStore.setCurrentOrganization({
            id: adminOrgId,
            name: adminOrgId, // Will be resolved later if needed
            role,
          });
        }
      }),
      catchError((error) => {
        this.authStore.setError(error.error?.message ?? 'Login failed');
        this.authStore.setLoading(false);
        return throwError(() => error);
      }),
      tap(() => {
        this.authStore.setLoading(false);
      })
    );
  }

  /**
   * Logout the current user
   */
  logout(): Observable<void> {
    const refreshToken = this.authStore.refreshToken();

    return this.http
      .post<void>(`${this.apiBaseUrl}/auth/logout`, { refreshToken })
      .pipe(
        tap(() => {
          this.authStore.logout();
        }),
        catchError((error) => {
          // Still logout locally even if the API call fails
          this.authStore.logout();
          return throwError(() => error);
        })
      );
  }

  /**
   * Logout locally without API call
   */
  logoutLocal(): void {
    this.authStore.logout();
  }

  /**
   * Refresh the access token
   */
  refreshToken(): Observable<{ accessToken: string; refreshToken?: string }> {
    const refreshToken = this.authStore.refreshToken();

    if (!refreshToken) {
      return throwError(() => new Error('No refresh token available'));
    }

    return this.http
      .post<{ accessToken: string; refreshToken?: string }>(
        `${this.apiBaseUrl}/auth/refresh`,
        { refreshToken }
      )
      .pipe(
        tap((response) => {
          this.authStore.setAccessToken(response.accessToken);
          if (response.refreshToken) {
            this.authStore.setRefreshToken(response.refreshToken);
          }
        }),
        catchError((error) => {
          // If refresh fails, logout the user
          this.authStore.logout();
          return throwError(() => error);
        })
      );
  }

  /**
   * Get the current user profile
   */
  getProfile(): Observable<AuthUser> {
    return this.http.get<AuthUser>(`${this.apiBaseUrl}/auth/profile`).pipe(
      tap((user) => {
        this.authStore.setUser(user);
      })
    );
  }

  /**
   * Update the current user profile
   */
  updateProfile(data: Partial<AuthUser>): Observable<AuthUser> {
    return this.http.patch<AuthUser>(`${this.apiBaseUrl}/auth/profile`, data).pipe(
      tap((user) => {
        this.authStore.setUser(user);
      })
    );
  }

  /**
   * Check if the current access token is expired
   */
  isTokenExpired(): boolean {
    const token = this.authStore.accessToken();
    if (!token) return true;

    try {
      const payload = this.decodeToken(token);
      const currentTime = Math.floor(Date.now() / 1000);
      return payload.exp < currentTime;
    } catch {
      return true;
    }
  }

  /**
   * Decode a JWT token
   */
  decodeToken(token: string): TokenPayload {
    const parts = token.split('.');
    if (parts.length !== 3) {
      throw new Error('Invalid token format');
    }

    const payload = parts[1];
    const decoded = atob(payload.replace(/-/g, '+').replace(/_/g, '/'));
    return JSON.parse(decoded) as TokenPayload;
  }

  /**
   * Get the current access token
   */
  getAccessToken(): string | null {
    return this.authStore.accessToken();
  }

  /**
   * Check if the user is authenticated
   */
  isAuthenticated(): boolean {
    return this.authStore.isAuthenticated();
  }

  /**
   * Get the current user
   */
  getUser(): AuthUser | null {
    return this.authStore.user();
  }

  /**
   * Check if the user is a super admin
   */
  isSuperAdmin(): boolean {
    return this.authStore.isSuperAdmin();
  }

  /**
   * Get the current tenant ID
   */
  getCurrentTenantId(): string | null {
    return this.authStore.currentTenantId();
  }

  /**
   * Set the current tenant ID (for super admin)
   */
  setCurrentTenantId(tenantId: string | null): void {
    this.authStore.setCurrentTenantId(tenantId);
  }
}
