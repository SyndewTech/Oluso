import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams, HttpContext } from '@angular/common/http';
import { SKIP_ERROR_TOAST } from '@syndew/oluso-ui-core';
import { Observable } from 'rxjs';
import { WorkspaceUserProfile } from '../models';

/**
 * API service for workspace endpoints
 */
@Injectable({
  providedIn: 'root',
})
export class WorkspaceApiService {
  private readonly http = inject(HttpClient);
  private baseUrl = '';

  /**
   * Set the base URL for API calls
   */
  setBaseUrl(url: string): void {
    this.baseUrl = url;
  }

  /**
   * Build an absolute URL that bypasses the /api/workspace prefix.
   * Use for anonymous platform endpoints like /api/tenant/resolve.
   */
  absoluteUrl(path: string): string {
    return `${this.baseUrl}${path}`;
  }

  /**
   * GET an endpoint at the server root (e.g. /api/tenant/resolve) rather than
   * under /api/workspace. The instance methods on this service explicitly
   * prefix /api/workspace, so calling them directly would 404 here.
   */
  getAbsolute<T>(path: string, params?: Record<string, string | number | boolean>): Observable<T> {
    let httpParams = new HttpParams();
    if (params) {
      Object.entries(params).forEach(([key, value]) => {
        if (value !== undefined && value !== null) {
          httpParams = httpParams.set(key, String(value));
        }
      });
    }
    return this.http.get<T>(this.absoluteUrl(path), { params: httpParams });
  }

  /** POST counterpart of {@link getAbsolute} — bypasses /api/workspace base. */
  postAbsolute<T>(path: string, body: unknown): Observable<T> {
    return this.http.post<T>(this.absoluteUrl(path), body);
  }

  /**
   * Get current user's workspace profile (identity data)
   */
  getMyProfile(): Observable<WorkspaceUserProfile> {
    return this.http.get<WorkspaceUserProfile>(`${this.baseUrl}/api/workspace/me`);
  }

  // ── User notification profile ───────────────────────────────────────────
  // Per-user snooze / quiet-hours / muted-events preferences. Consulted by
  // IUserNotificationGate before any user-targeted notification is sent —
  // so what the user edits via these endpoints actually controls what they
  // receive across every channel (email today; push/SMS when wired).
  // Reusable from any workspace app — these are platform-level prefs, not
  // approval-scoped.

  getMyNotificationProfile(): Observable<UserNotificationProfile> {
    return this.http.get<UserNotificationProfile>(
      `${this.baseUrl}/api/workspace/me/notification-profile`,
    );
  }

  updateMyNotificationProfile(data: UserNotificationProfile): Observable<UserNotificationProfile> {
    return this.http.put<UserNotificationProfile>(
      `${this.baseUrl}/api/workspace/me/notification-profile`,
      data,
    );
  }

  /** Quick-snooze convenience — server computes the absolute UTC instant. */
  snoozeNotifications(durationMinutes: number, reason?: string): Observable<UserNotificationProfile> {
    return this.http.post<UserNotificationProfile>(
      `${this.baseUrl}/api/workspace/me/notification-profile/snooze`,
      { durationMinutes, reason },
    );
  }

  clearNotificationSnooze(): Observable<UserNotificationProfile> {
    return this.http.delete<UserNotificationProfile>(
      `${this.baseUrl}/api/workspace/me/notification-profile/snooze`,
    );
  }

  // --------------------------------------------------------------------
  // Conversation threads (generic, per-entity)
  // --------------------------------------------------------------------
  // Modules attach comments by serving an IConversationAuthorizer for
  // their entity type on the server. The workspace-ui ConversationThread
  // component is a thin renderer over these four endpoints.

  listConversationMessages(entityType: string, entityId: string): Observable<ConversationMessage[]> {
    // 403 (can't see thread) / 404 (no thread yet) are normal here — the
    // component renders an empty state, so suppress the global error toast.
    return this.http.get<ConversationMessage[]>(
      `${this.baseUrl}/api/workspace/conversations/${encodeURIComponent(entityType)}/${encodeURIComponent(entityId)}`,
      { context: new HttpContext().set(SKIP_ERROR_TOAST, true) },
    );
  }

  /** Users who can be tagged with @ on this thread (drives autocomplete). */
  listConversationParticipants(entityType: string, entityId: string): Observable<ConversationParticipant[]> {
    return this.http.get<ConversationParticipant[]>(
      `${this.baseUrl}/api/workspace/conversations/${encodeURIComponent(entityType)}/${encodeURIComponent(entityId)}/participants`,
      { context: new HttpContext().set(SKIP_ERROR_TOAST, true) },
    );
  }

  postConversationMessage(
    entityType: string,
    entityId: string,
    body: string,
    mentionedUserIds?: string[],
  ): Observable<ConversationMessage> {
    return this.http.post<ConversationMessage>(
      `${this.baseUrl}/api/workspace/conversations/${encodeURIComponent(entityType)}/${encodeURIComponent(entityId)}`,
      { body, mentionedUserIds },
    );
  }

  editConversationMessage(
    entityType: string,
    entityId: string,
    messageId: string,
    body: string,
    mentionedUserIds?: string[],
  ): Observable<ConversationMessage> {
    return this.http.put<ConversationMessage>(
      `${this.baseUrl}/api/workspace/conversations/${encodeURIComponent(entityType)}/${encodeURIComponent(entityId)}/${messageId}`,
      { body, mentionedUserIds },
    );
  }

  deleteConversationMessage(entityType: string, entityId: string, messageId: string): Observable<void> {
    return this.http.delete<void>(
      `${this.baseUrl}/api/workspace/conversations/${encodeURIComponent(entityType)}/${encodeURIComponent(entityId)}/${messageId}`,
    );
  }
}

export interface ConversationMessage {
  id: string;
  entityType: string;
  entityId: string;
  authorUserId: string;
  authorName?: string;
  /** Null when the message has been deleted — UI renders a placeholder. */
  body?: string;
  createdAt: string;
  editedAt?: string;
  deletedAt?: string;
  /** User ids tagged with @ in this message. */
  mentionedUserIds?: string[];
}

export interface ConversationParticipant {
  userId: string;
  displayName?: string;
  email?: string;
}

/**
 * Per-user notification preferences shape returned by
 * /api/workspace/me/notification-profile. Mirrors UserNotificationProfileDto
 * on the server. All fields nullable — absence = "deliver normally."
 */
export interface UserNotificationProfile {
  /** UTC ISO timestamp; null/in-past means "not currently snoozed." */
  snoozeUntilUtc?: string | null;
  snoozeReason?: string | null;
  /** Minutes-since-midnight, [0,1440). Quiet hours support overnight (start > end). */
  quietHoursStartMinute?: number | null;
  quietHoursEndMinute?: number | null;
  /** IANA tz id. Falls back to UTC server-side when unrecognised. */
  timeZoneId?: string | null;
  /** Comma-separated event types; supports {category}.* wildcards. */
  mutedEventTypes?: string | null;
}
