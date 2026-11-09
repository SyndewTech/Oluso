import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { LocaleService } from '@syndew/oluso-ui-core';
import { ApiService } from './api.service';

/** A single key in the Terminology Center grid (matches backend ResolvedTerminologyKey). */
export interface ResolvedTerminologyKey {
  moduleKey: string;
  key: string;
  group: string;
  description?: string;
  isOverridable: boolean;
  supportsPlural: boolean;
  inheritedValue: string;
  overrideValue?: string | null;
}

export interface TerminologyOverrideEntry {
  key: string;
  locale: string;
  value: string;
}

export interface IndustryOption {
  value: string;
  label: string;
}

export interface TenantTerminologyProfile {
  industry?: string | null;
  availableIndustries: IndustryOption[];
}

/**
 * Client for the central Terminology Center (`/api/admin/terminology`). Module
 * agnostic — the backend aggregates every licensed module's keys, so this
 * service has no per-module knowledge.
 */
@Injectable({ providedIn: 'root' })
export class TerminologyService {
  private readonly api = inject(ApiService);
  private readonly locale = inject(LocaleService);
  private readonly endpoint = '/terminology';

  /** The authoring grid for the tenant's licensed modules. */
  getKeys(locale = this.locale.locale()): Observable<ResolvedTerminologyKey[]> {
    return this.api.get<ResolvedTerminologyKey[]>(`${this.endpoint}/keys`, { locale });
  }

  /** Set (create/update) a tenant override for one key. */
  setOverride(key: string, value: string, locale = this.locale.locale()): Observable<TerminologyOverrideEntry> {
    return this.api.put<TerminologyOverrideEntry>(`${this.endpoint}/overrides`, { key, value, locale });
  }

  /** Reset a key to its inherited value. */
  resetOverride(key: string, locale = this.locale.locale()): Observable<void> {
    return this.api.delete<void>(`${this.endpoint}/overrides/${encodeURIComponent(key)}`, { locale });
  }

  /**
   * The tenant's industry/type and the available choices. This is a central
   * platform setting (its own `/tenant-profile` endpoint), not a terminology
   * sub-resource — terminology only consumes it.
   */
  getProfile(): Observable<TenantTerminologyProfile> {
    return this.api.get<TenantTerminologyProfile>('/tenant-profile');
  }

  /** Set the tenant's industry/type (a deliberate platform-settings action). */
  setProfile(industry: string | null): Observable<void> {
    return this.api.put<void>('/tenant-profile', { industry });
  }
}
