import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { SenderProfile } from '../models/sender-profile.model';

/**
 * Sender profile calls for one tenant. Unscoped, the backend uses the tenant in
 * context; scoped, calls go to /tenants/{tenantId}/sender-profiles so the tab on
 * a tenant's settings page edits that tenant's profiles, access-checked server-side.
 */
export interface TenantReplyTo {
  /** Confirmed inbox in use; null when none is confirmed. */
  replyToAddress?: string | null;
  verifiedAt?: string | null;
  /** Address a confirmation link was sent to; not used until confirmed. */
  pendingAddress?: string | null;
  pendingSentAt?: string | null;
}

export class SenderProfilesClient {
  private readonly endpoint: string;

  constructor(protected readonly api: ApiService, tenantId?: string) {
    this.endpoint = tenantId ? `/tenants/${tenantId}/sender-profiles` : '/sender-profiles';
  }

  list(): Observable<SenderProfile[]> {
    return this.api.get<SenderProfile[]>(this.endpoint);
  }

  get(id: string): Observable<SenderProfile> {
    return this.api.get<SenderProfile>(`${this.endpoint}/${id}`);
  }

  create(input: SenderProfile): Observable<SenderProfile> {
    return this.api.post<SenderProfile>(this.endpoint, input);
  }

  update(id: string, input: SenderProfile): Observable<SenderProfile> {
    return this.api.put<SenderProfile>(`${this.endpoint}/${id}`, input);
  }

  delete(id: string): Observable<void> {
    return this.api.delete<void>(`${this.endpoint}/${id}`);
  }

  setDefault(id: string): Observable<void> {
    return this.api.post<void>(`${this.endpoint}/${id}/set-default`, {});
  }

  /** The tenant's confirmed reply-to inbox and any address awaiting confirmation. */
  getReplyTo(): Observable<TenantReplyTo> {
    return this.api.get<TenantReplyTo>(`${this.endpoint}/reply-to`);
  }

  /** Mails a confirmation link to the address; null removes the reply-to. */
  requestReplyTo(replyToAddress: string | null): Observable<TenantReplyTo> {
    return this.api.put<TenantReplyTo>(`${this.endpoint}/reply-to`, { replyToAddress });
  }

  resendReplyTo(): Observable<TenantReplyTo> {
    return this.api.post<TenantReplyTo>(`${this.endpoint}/reply-to/resend`, {});
  }

  cancelPendingReplyTo(): Observable<TenantReplyTo> {
    return this.api.delete<TenantReplyTo>(`${this.endpoint}/reply-to/pending`);
  }

  sendTest(id: string, to: string): Observable<{ message: string; messageId?: string }> {
    return this.api.post<{ message: string; messageId?: string }>(`${this.endpoint}/${id}/test`, { to });
  }
}

@Injectable({ providedIn: 'root' })
export class SenderProfilesService extends SenderProfilesClient {
  constructor() {
    super(inject(ApiService));
  }

  /** Calls addressed to a specific tenant (tenant settings page); unscoped when omitted. */
  forTenant(tenantId?: string): SenderProfilesClient {
    return tenantId ? new SenderProfilesClient(this.api, tenantId) : this;
  }
}
