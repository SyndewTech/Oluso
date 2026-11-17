import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { PlatformDomain } from '../models/platform.model';

/**
 * Every organization's custom domains. Verifying, removing and restoring a domain use the organization's own domain
 * endpoints, the same ones its Domains tab calls.
 */
@Injectable({ providedIn: 'root' })
export class PlatformDomainService {
  private readonly api = inject(ApiService);

  getAll(): Observable<PlatformDomain[]> {
    return this.api.get<PlatformDomain[]>('/platform/domains');
  }

  verify(domain: PlatformDomain): Observable<{ status: string; failureReason?: string | null }> {
    return this.api.post(`/tenants/${domain.tenantId}/domains/${domain.id}/verify`, {});
  }

  remove(domain: PlatformDomain): Observable<void> {
    return this.api.delete<void>(`/tenants/${domain.tenantId}/domains/${domain.id}`);
  }

  /** Adds a removed domain back to the organization that held it; it starts verification again. */
  restore(domain: PlatformDomain): Observable<unknown> {
    return this.api.post(`/tenants/${domain.tenantId}/domains`, { domain: domain.domain });
  }
}
