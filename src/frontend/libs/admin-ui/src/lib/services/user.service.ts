import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { PaginationParams } from '../models/pagination-params.model';
import { PaginatedResponse } from '@oluso/ui-core';
import { UserDto, UserRoleDto } from '../models/user.model';
import { UserClaimDto } from '../models/user-claim.model';
import { UserSessionDto } from '../models/user-session.model';
import { ExternalLoginDto } from '../models/external-login.model';
import { CreateUserRequest } from '../models/create-user-request.model';

/**
 * User Service
 *
 * Manages user operations.
 * This mirrors the userService from the React AdminUI.
 */
@Injectable({
  providedIn: 'root',
})
export class UserService {
  private readonly api = inject(ApiService);

  private readonly endpoint = '/users';

  /**
   * Get all users with pagination
   */
  getUsers(
    pagination: PaginationParams,
    filters?: { search?: string; enabled?: boolean; role?: string }
  ): Observable<PaginatedResponse<UserDto>> {
    return this.api.getPaginated<UserDto>(this.endpoint, pagination, filters as Record<string, string | number | boolean>);
  }

  /**
   * Get a single user by ID
   */
  getUser(id: string): Observable<UserDto> {
    return this.api.get<UserDto>(`${this.endpoint}/${id}`);
  }

  /**
   * Create a new user
   */
  createUser(user: CreateUserRequest): Observable<UserDto> {
    return this.api.post<UserDto>(this.endpoint, user);
  }

  /**
   * Update an existing user
   */
  updateUser(id: string, user: UserDto): Observable<UserDto> {
    return this.api.put<UserDto>(`${this.endpoint}/${id}`, user);
  }

  /**
   * Delete a user
   */
  deleteUser(id: string): Observable<void> {
    return this.api.delete<void>(`${this.endpoint}/${id}`);
  }

  /**
   * Enable a user
   */
  enableUser(user: UserDto): Observable<UserDto> {
    return this.api.put<UserDto>(`${this.endpoint}/${user.id}`, { ...user, enabled: true });
  }

  /**
   * Disable a user
   */
  disableUser(user: UserDto): Observable<UserDto> {
    return this.api.put<UserDto>(`${this.endpoint}/${user.id}`, { ...user, enabled: false });
  }

  /**
   * Lock a user
   */
  lockUser(id: string, lockoutEnd?: string): Observable<UserDto> {
    return this.api.post<UserDto>(`${this.endpoint}/${id}/lock`, { lockoutEnd });
  }

  /**
   * Unlock a user
   */
  unlockUser(id: string): Observable<UserDto> {
    return this.api.post<UserDto>(`${this.endpoint}/${id}/unlock`, {});
  }

  /**
   * Reset user password
   */
  resetPassword(id: string, newPassword: string): Observable<void> {
    return this.api.post<void>(`${this.endpoint}/${id}/reset-password`, { newPassword });
  }

  /**
   * Send the set-password invite again to a user who hasn't set a password yet.
   */
  sendInvite(id: string): Observable<{ message: string }> {
    return this.api.post<{ message: string }>(`${this.endpoint}/${id}/invite`, {});
  }

  /**
   * Send password reset email
   */
  sendPasswordResetEmail(id: string): Observable<void> {
    return this.api.post<void>(`${this.endpoint}/${id}/send-password-reset`, {});
  }

  /**
   * Send verification email
   */
  sendVerificationEmail(id: string): Observable<void> {
    return this.api.post<void>(`${this.endpoint}/${id}/send-verification`, {});
  }

  /**
   * Verify user email
   */
  verifyEmail(user: UserDto): Observable<UserDto> {
    return this.api.put<UserDto>(`${this.endpoint}/${user.id}`, { ...user, emailVerified: true });
  }

  /**
   * Roles the user holds in the current tenant, with ids.
   */
  getUserRoles(id: string): Observable<UserRoleDto[]> {
    return this.api.get<UserRoleDto[]>(`${this.endpoint}/${id}/roles`);
  }

  /**
   * Replace the user's roles in the current tenant with exactly these role ids.
   */
  assignRoles(id: string, roleIds: string[]): Observable<void> {
    return this.api.put<void>(`${this.endpoint}/${id}/roles`, { roleIds });
  }

  /**
   * Get user claims
   */
  getClaims(id: string): Observable<UserClaimDto[]> {
    return this.api.get<UserClaimDto[]>(`${this.endpoint}/${id}/claims`);
  }

  /**
   * Add a claim to user
   */
  addClaim(id: string, claim: { type: string; value: string }): Observable<void> {
    return this.api.post<void>(`${this.endpoint}/${id}/claims`, claim);
  }

  /**
   * Delete a claim from user
   */
  deleteClaim(id: string, type: string, value: string): Observable<void> {
    return this.api.delete<void>(`${this.endpoint}/${id}/claims/${encodeURIComponent(type)}/${encodeURIComponent(value)}`);
  }

  /**
   * Get user sessions
   */
  getSessions(id: string): Observable<UserSessionDto[]> {
    return this.api.get<UserSessionDto[]>(`${this.endpoint}/${id}/sessions`);
  }

  /**
   * Revoke a user session
   */
  revokeSession(id: string, sessionId: string): Observable<void> {
    return this.api.delete<void>(`${this.endpoint}/${id}/sessions/${sessionId}`);
  }

  /**
   * Revoke all user sessions
   */
  revokeAllSessions(id: string): Observable<void> {
    return this.api.delete<void>(`${this.endpoint}/${id}/sessions`);
  }

  /**
   * Get user external logins
   */
  getExternalLogins(id: string): Observable<ExternalLoginDto[]> {
    return this.api.get<ExternalLoginDto[]>(`${this.endpoint}/${id}/external-logins`);
  }

  /**
   * Remove external login
   */
  removeExternalLogin(id: string, provider: string, providerKey: string): Observable<void> {
    return this.api.delete<void>(`${this.endpoint}/${id}/external-logins/${encodeURIComponent(provider)}/${encodeURIComponent(providerKey)}`);
  }

  /**
   * Reset MFA for user
   */
  resetMfa(id: string): Observable<void> {
    return this.api.post<void>(`${this.endpoint}/${id}/reset-mfa`, {});
  }
}
