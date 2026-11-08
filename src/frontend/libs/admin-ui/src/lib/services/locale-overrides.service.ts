import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';

export interface LocaleStringsEditorResponse {
  locale: string;
  defaults: Record<string, string>;
  overrides: Record<string, string>;
}

@Injectable({ providedIn: 'root' })
export class LocaleOverridesService {
  private readonly api = inject(ApiService);

  getAvailableLocales(): Observable<string[]> {
    return this.api.get<string[]>('/locale-strings/locales');
  }

  getSystemDefaults(locale: string): Observable<Record<string, string>> {
    return this.api.get<Record<string, string>>(`/locale-strings/defaults/${locale}`);
  }

  getTenantOverrides(locale: string): Observable<LocaleStringsEditorResponse> {
    return this.api.get<LocaleStringsEditorResponse>(`/locale-strings/tenant/${locale}`);
  }

  saveTenantOverrides(locale: string, overrides: Record<string, string>): Observable<void> {
    return this.api.put<void>(`/locale-strings/tenant/${locale}`, { overrides });
  }

  deleteTenantOverrides(locale: string): Observable<void> {
    return this.api.delete<void>(`/locale-strings/tenant/${locale}`);
  }

  getJourneyOverrides(policyId: string, locale: string): Observable<LocaleStringsEditorResponse> {
    return this.api.get<LocaleStringsEditorResponse>(`/journeys/${policyId}/locale-overrides/${locale}`);
  }

  saveJourneyOverrides(policyId: string, locale: string, overrides: Record<string, string>): Observable<void> {
    return this.api.put<void>(`/journeys/${policyId}/locale-overrides/${locale}`, { overrides });
  }
}
